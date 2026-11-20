package config

import (
	"fmt"
	"os"
	"strings"

	"github.com/redis/go-redis/v9"
)

// ParseRedisConnectionString handles Aspire's format: "host:port,password=xxx,ssl=xxx"
func ParseRedisOptions(connStr string) (*redis.Options, error) {
	if connStr == "" {
		return nil, fmt.Errorf("redis connection string is empty")
	}

	// If it is a redis URL (redis://), we use the standard parser
	if strings.HasPrefix(connStr, "redis://") || strings.HasPrefix(connStr, "rediss://") {
		opts, err := redis.ParseURL(connStr)
		if err != nil {
			return nil, err
		}
		opts.ContextTimeoutEnabled = true
		return opts, nil
	}

	// ContextTimeoutEnabled: honour the context deadlines of the request path (INGEST_DEPENDENCY_TIMEOUT).
	// Without it go-redis ignores them and waits for its own 3 s read timeout when Redis stops answering.
	opts := &redis.Options{ContextTimeoutEnabled: true}
	parts := strings.Split(connStr, ",")

	// The first element is always host:port
	opts.Addr = parts[0]

	// Process the remaining parameters
	for _, part := range parts[1:] {
		kv := strings.SplitN(part, "=", 2)
		if len(kv) != 2 {
			continue
		}
		key := strings.ToLower(strings.TrimSpace(kv[0]))
		val := strings.TrimSpace(kv[1])

		switch key {
		case "password":
			opts.Password = val
		case "ssl":
			// Disable TLS entirely for this environment to fix SSL routines::wrong version number
			opts.TLSConfig = nil
		}
	}

	// Fallback to global REDIS_PASSWORD if not set in connection string
	if opts.Password == "" {
		if globalPwd := os.Getenv("REDIS_PASSWORD"); globalPwd != "" {
			opts.Password = globalPwd
		}
	}

	return opts, nil
}

func GetEnv(key, fallback string) string {
	if value, ok := os.LookupEnv(key); ok {
		return value
	}
	return fallback
}
