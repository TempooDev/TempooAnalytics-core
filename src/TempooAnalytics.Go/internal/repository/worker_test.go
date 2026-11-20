package repository

import (
	"errors"
	"testing"
	"time"

	"github.com/TempooDev/TempooAnalytics-core/src/TempooAnalytics.Go/internal/model"
)

func TestWorker_EnqueueFullBufferFailsFast(t *testing.T) {
	w := NewClickHouseWorker(nil, WorkerConfig{BufferCapacity: 2}) // not started: nothing drains it
	for i := 0; i < 2; i++ {
		if err := w.Enqueue(&model.AnalyticsEvent{}); err != nil {
			t.Fatalf("event %d: %v", i, err)
		}
	}
	start := time.Now()
	if err := w.Enqueue(&model.AnalyticsEvent{}); !errors.Is(err, ErrBufferFull) {
		t.Fatalf("got %v, want ErrBufferFull", err)
	}
	if d := time.Since(start); d > 10*time.Millisecond {
		t.Fatalf("Enqueue blocked for %v", d)
	}
	if w.QueueDepth() != 2 {
		t.Fatalf("depth = %d, want 2", w.QueueDepth())
	}
}

func TestWorkerConfig_Defaults(t *testing.T) {
	c := WorkerConfig{BatchSize: 10}.withDefaults()
	if c.BatchSize != 10 || c.FlushInterval != FlushInterval || c.BufferCapacity != BufferCapacity || c.NumWorkers != NumWorkers {
		t.Fatalf("got %+v", c)
	}
}
