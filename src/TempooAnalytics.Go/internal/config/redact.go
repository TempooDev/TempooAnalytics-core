package config

import "regexp"

var (
	// scheme://user:password@host -> scheme://user:***@host
	urlPasswordRe = regexp.MustCompile(`(://[^:/@\s]*:)[^@/\s]+(@)`)
	// password=xxx / pwd=xxx in "host:port,password=xxx" (Aspire) and "key=value" connection strings
	keyPasswordRe = regexp.MustCompile(`(?i)\b(password|pwd)=[^,;&\s]*`)
)

// RedactSecrets hides the passwords of a connection string so it can be logged.
func RedactSecrets(s string) string {
	s = urlPasswordRe.ReplaceAllString(s, "${1}***${2}")
	return keyPasswordRe.ReplaceAllString(s, "${1}=***")
}
