package config

import "testing"

func TestParseRedisOptions_HonoursContextDeadlines(t *testing.T) {
	for _, conn := range []string{"redis:6379,password=x", "redis://:x@redis:6379/0"} {
		opts, err := ParseRedisOptions(conn)
		if err != nil {
			t.Fatalf("%s: %v", conn, err)
		}
		if !opts.ContextTimeoutEnabled {
			t.Errorf("%s: ContextTimeoutEnabled is off, request deadlines would be ignored", conn)
		}
		if opts.Addr != "redis:6379" || opts.Password != "x" {
			t.Errorf("%s: parsed %s / %q", conn, opts.Addr, opts.Password)
		}
	}
}
