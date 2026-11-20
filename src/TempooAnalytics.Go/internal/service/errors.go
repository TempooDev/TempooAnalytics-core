package service

import (
	"errors"
	"time"
)

// Errors ProcessEvent returns; the handler maps each one to an HTTP status and a stable code
// (handler.errorResponse). Check them with errors.Is.
var (
	// ErrSiteNotFound: no site has that siteId (404 site_not_found).
	ErrSiteNotFound = errors.New("site not found")
	// ErrUnauthorized: no write key and no Origin/Referer, or a wrong write key without an allowed
	// origin; with RequireWriteKey, any missing or wrong write key (401 unauthorized).
	ErrUnauthorized = errors.New("missing or invalid write key")
	// ErrOriginNotAllowed: the Origin/Referer is not one of the site's allowed origins and there is no
	// valid write key (403 origin_not_allowed).
	ErrOriginNotAllowed = errors.New("origin not allowed")
	// ErrTrialExpired: free plan older than the trial (402 trial_expired).
	ErrTrialExpired = errors.New("trial expired")
	// ErrQuotaExceeded: the site used its monthly events (429 quota_exceeded). The error is a
	// *QuotaExceededError with the time left until the block ends.
	ErrQuotaExceeded = errors.New("monthly event limit reached for this site")
)

// QuotaExceededError carries how long the site stays blocked, for Retry-After.
type QuotaExceededError struct {
	RetryAfter time.Duration
}

func (e *QuotaExceededError) Error() string { return ErrQuotaExceeded.Error() }

func (e *QuotaExceededError) Is(target error) bool { return target == ErrQuotaExceeded }
