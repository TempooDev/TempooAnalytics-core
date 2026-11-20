package metrics

import (
	"strconv"
	"time"

	"github.com/gin-gonic/gin"
)

// GinMiddleware returns a Gin middleware that records HTTP request metrics
func GinMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		start := time.Now()

		c.Next()

		duration := time.Since(start).Seconds()
		status := strconv.Itoa(c.Writer.Status())
		route := c.FullPath()
		if route == "" {
			route = "unknown"
		}
		method := c.Request.Method

		HTTPRequestDuration.WithLabelValues(method, route, status).Observe(duration)
		HTTPRequestsTotal.WithLabelValues(method, route, status).Inc()
	}
}
