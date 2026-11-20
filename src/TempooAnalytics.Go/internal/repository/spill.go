package repository

import (
	"bufio"
	"encoding/json"
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
)

const (
	spillExt        = ".jsonl"
	spillBadExt     = ".bad"
	spillTmpPattern = ".tmp-*"
)

// FileSpill saves the batches ClickHouse did not accept after every retry, one JSON-lines file per batch,
// so they survive a restart of ingestion (mount the directory as a volume) and can be replayed later.
// File name: <unix nanos>-<deduplication token>.jsonl, so files sort oldest first and the replay reuses
// the token of the original attempts.
type FileSpill struct {
	dir string
}

func NewFileSpill(dir string) (*FileSpill, error) {
	if err := os.MkdirAll(dir, 0o750); err != nil {
		return nil, err
	}
	// Check it is writable now rather than when ClickHouse is already failing.
	f, err := os.CreateTemp(dir, spillTmpPattern)
	if err != nil {
		return nil, err
	}
	name := f.Name()
	f.Close()
	os.Remove(name)
	return &FileSpill{dir: dir}, nil
}

// Save writes the batch atomically (temporary file, fsync, rename).
func (s *FileSpill) Save(token string, events []*model.AnalyticsEvent) error {
	f, err := os.CreateTemp(s.dir, spillTmpPattern)
	if err != nil {
		return err
	}
	tmp := f.Name()
	defer os.Remove(tmp) // no-op after the rename

	w := bufio.NewWriter(f)
	enc := json.NewEncoder(w)
	for _, e := range events {
		if err := enc.Encode(e); err != nil {
			f.Close()
			return err
		}
	}
	if err := w.Flush(); err != nil {
		f.Close()
		return err
	}
	if err := f.Sync(); err != nil {
		f.Close()
		return err
	}
	if err := f.Close(); err != nil {
		return err
	}
	name := fmt.Sprintf("%020d-%s%s", time.Now().UnixNano(), token, spillExt)
	return os.Rename(tmp, filepath.Join(s.dir, name))
}

// List returns the saved batches, oldest first.
func (s *FileSpill) List() ([]string, error) {
	entries, err := os.ReadDir(s.dir)
	if err != nil {
		return nil, err
	}
	var names []string
	for _, e := range entries {
		if !e.IsDir() && strings.HasSuffix(e.Name(), spillExt) {
			names = append(names, e.Name())
		}
	}
	sort.Strings(names)
	return names, nil
}

// Pending returns how many batches are waiting to be replayed.
func (s *FileSpill) Pending() int {
	names, _ := s.List()
	return len(names)
}

// Load reads a saved batch and its deduplication token.
func (s *FileSpill) Load(name string) (string, []*model.AnalyticsEvent, error) {
	base := strings.TrimSuffix(name, spillExt)
	_, token, ok := strings.Cut(base, "-")
	if !ok || token == "" {
		return "", nil, fmt.Errorf("nombre de lote sin token: %s", name)
	}
	f, err := os.Open(filepath.Join(s.dir, name))
	if err != nil {
		return "", nil, err
	}
	defer f.Close()
	var events []*model.AnalyticsEvent
	dec := json.NewDecoder(bufio.NewReader(f))
	for dec.More() {
		var e model.AnalyticsEvent
		if err := dec.Decode(&e); err != nil {
			return "", nil, err
		}
		events = append(events, &e)
	}
	return token, events, nil
}

// Remove deletes a batch that was replayed.
func (s *FileSpill) Remove(name string) error {
	return os.Remove(filepath.Join(s.dir, name))
}

// Quarantine renames an unreadable batch so it is not retried forever but stays for a person to look at.
func (s *FileSpill) Quarantine(name string) {
	_ = os.Rename(filepath.Join(s.dir, name), filepath.Join(s.dir, name+spillBadExt))
}
