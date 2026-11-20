// Package geoip makes sure the GeoLite2 databases are on disk.
//
// Existing files are used as they are. When a file is missing (or older than MaxAge) and a MaxMind
// account/key is configured, the official archive is downloaded. Without a file and without a key the
// caller keeps running with geolocation disabled.
package geoip

import (
	"archive/tar"
	"compress/gzip"
	"context"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"time"
)

const defaultURLTemplate = "https://download.maxmind.com/geoip/databases/%s/download?suffix=tar.gz"

// Provisioner downloads GeoLite2 editions on demand.
type Provisioner struct {
	AccountID   string
	LicenseKey  string
	MaxAge      time.Duration // refresh files older than this; default 7 days
	URLTemplate string        // fmt template with one %s for the edition; default MaxMind's
	Client      *http.Client  // default: 2 minute timeout
}

func (p *Provisioner) canDownload() bool { return p.AccountID != "" && p.LicenseKey != "" }

// Ensure guarantees, as far as it can, that path holds the given edition (for example "GeoLite2-Country").
// It never fails the caller: problems are logged and an existing file is always kept.
func (p *Provisioner) Ensure(ctx context.Context, edition, path string) {
	info, err := os.Stat(path)
	exists := err == nil
	maxAge := p.MaxAge
	if maxAge == 0 {
		maxAge = 7 * 24 * time.Hour
	}
	stale := exists && time.Since(info.ModTime()) > maxAge
	if exists && !stale {
		return
	}
	if !p.canDownload() {
		if !exists {
			log.Printf("GeoIP: %s not found at %s and MAXMIND_ACCOUNT_ID / MAXMIND_LICENSE_KEY are not set; geolocation for this database is disabled", edition, path)
		}
		return
	}
	if err := p.download(ctx, edition, path); err != nil {
		log.Printf("GeoIP: download of %s failed (keeping existing file: %t): %v", edition, exists, err)
		return
	}
	log.Printf("GeoIP: downloaded %s to %s", edition, path)
}

func (p *Provisioner) download(ctx context.Context, edition, path string) error {
	tmpl := p.URLTemplate
	if tmpl == "" {
		tmpl = defaultURLTemplate
	}
	client := p.Client
	if client == nil {
		client = &http.Client{Timeout: 2 * time.Minute}
	}
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, fmt.Sprintf(tmpl, edition), nil)
	if err != nil {
		return err
	}
	req.SetBasicAuth(p.AccountID, p.LicenseKey)
	resp, err := client.Do(req)
	if err != nil {
		return err
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusOK {
		// The status is enough to tell a bad key (401) from a quota problem (429); never log the key.
		return fmt.Errorf("HTTP %d", resp.StatusCode)
	}

	gz, err := gzip.NewReader(resp.Body)
	if err != nil {
		return err
	}
	defer gz.Close()
	tr := tar.NewReader(gz)
	for {
		hdr, err := tr.Next()
		if errors.Is(err, io.EOF) {
			return errors.New("archive contains no .mmdb file")
		}
		if err != nil {
			return err
		}
		if hdr.Typeflag != tar.TypeReg || !strings.HasSuffix(strings.ToLower(hdr.Name), ".mmdb") {
			continue
		}
		if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
			return err
		}
		tmp := path + ".download"
		f, err := os.Create(tmp)
		if err != nil {
			return err
		}
		if _, err := io.Copy(f, tr); err != nil {
			f.Close()
			os.Remove(tmp)
			return err
		}
		if err := f.Close(); err != nil {
			os.Remove(tmp)
			return err
		}
		return os.Rename(tmp, path)
	}
}
