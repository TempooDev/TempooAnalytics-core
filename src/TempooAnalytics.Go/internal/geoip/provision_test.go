package geoip

import (
	"archive/tar"
	"bytes"
	"compress/gzip"
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"sync/atomic"
	"testing"
	"time"
)

func archive(t *testing.T, name string, content []byte) []byte {
	t.Helper()
	var buf bytes.Buffer
	gz := gzip.NewWriter(&buf)
	tw := tar.NewWriter(gz)
	if err := tw.WriteHeader(&tar.Header{Name: name, Mode: 0o644, Size: int64(len(content)), Typeflag: tar.TypeReg}); err != nil {
		t.Fatal(err)
	}
	tw.Write(content)
	tw.Close()
	gz.Close()
	return buf.Bytes()
}

func server(t *testing.T, status int, body []byte) (*httptest.Server, *int32, *string) {
	t.Helper()
	var hits int32
	var user string
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		atomic.AddInt32(&hits, 1)
		u, _, _ := r.BasicAuth()
		user = u
		w.WriteHeader(status)
		w.Write(body)
	}))
	t.Cleanup(srv.Close)
	return srv, &hits, &user
}

func TestEnsure_NoFileNoKey_DoesNothing(t *testing.T) {
	srv, hits, _ := server(t, 200, nil)
	p := &Provisioner{URLTemplate: srv.URL + "/%s"}
	path := filepath.Join(t.TempDir(), "GeoLite2-Country.mmdb")

	p.Ensure(context.Background(), "GeoLite2-Country", path)

	if *hits != 0 {
		t.Fatalf("expected no requests, got %d", *hits)
	}
	if _, err := os.Stat(path); err == nil {
		t.Fatal("file must not exist")
	}
}

func TestEnsure_DownloadsAndExtractsWithBasicAuth(t *testing.T) {
	srv, hits, user := server(t, 200, archive(t, "GeoLite2-Country_2026/GeoLite2-Country.mmdb", []byte{1, 2, 3}))
	p := &Provisioner{AccountID: "123", LicenseKey: "k", URLTemplate: srv.URL + "/%s"}
	path := filepath.Join(t.TempDir(), "nested", "GeoLite2-Country.mmdb")

	p.Ensure(context.Background(), "GeoLite2-Country", path)

	got, err := os.ReadFile(path)
	if err != nil || !bytes.Equal(got, []byte{1, 2, 3}) {
		t.Fatalf("unexpected file content %v err %v", got, err)
	}
	if *hits != 1 || *user != "123" {
		t.Fatalf("hits=%d user=%q", *hits, *user)
	}
}

func TestEnsure_FreshFileIsLeftAlone(t *testing.T) {
	srv, hits, _ := server(t, 200, nil)
	p := &Provisioner{AccountID: "1", LicenseKey: "k", URLTemplate: srv.URL + "/%s"}
	path := filepath.Join(t.TempDir(), "x.mmdb")
	os.WriteFile(path, []byte{9}, 0o644)

	p.Ensure(context.Background(), "GeoLite2-Country", path)

	if *hits != 0 {
		t.Fatalf("expected no requests, got %d", *hits)
	}
}

func TestEnsure_StaleFileSurvivesFailedRefresh(t *testing.T) {
	srv, hits, _ := server(t, 401, nil)
	p := &Provisioner{AccountID: "1", LicenseKey: "bad", URLTemplate: srv.URL + "/%s"}
	path := filepath.Join(t.TempDir(), "x.mmdb")
	os.WriteFile(path, []byte{9}, 0o644)
	old := time.Now().Add(-30 * 24 * time.Hour)
	os.Chtimes(path, old, old)

	p.Ensure(context.Background(), "GeoLite2-Country", path)

	got, _ := os.ReadFile(path)
	if *hits != 1 || !bytes.Equal(got, []byte{9}) {
		t.Fatalf("hits=%d content=%v", *hits, got)
	}
}
