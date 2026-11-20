package config

import "testing"

func TestRedactSecrets(t *testing.T) {
	cases := map[string]string{
		"postgres://user:s3cr3t@db:5432/tempoo?sslmode=disable": "postgres://user:***@db:5432/tempoo?sslmode=disable",
		"redis://:s3cr3t@redis:6379":                            "redis://:***@redis:6379",
		"redis:6379,password=s3cr3t,ssl=false":                  "redis:6379,password=***,ssl=false",
		"host=db port=5432 user=u password=s3cr3t dbname=x":     "host=db port=5432 user=u password=*** dbname=x",
		"infrastructure-redis-rj4huy:6379":                      "infrastructure-redis-rj4huy:6379",
		"postgres://user@db:5432/tempoo":                        "postgres://user@db:5432/tempoo",
	}
	for in, want := range cases {
		if got := RedactSecrets(in); got != want {
			t.Errorf("RedactSecrets(%q) = %q, want %q", in, got, want)
		}
	}
}
