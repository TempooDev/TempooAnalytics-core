# Technical Guide: Handling Custom Events

## 1. Overview
Unlike a pageview, which is standard, a Custom Event lets the customer measure specific actions (clicking "Add to cart", "Subscription", "Form error").

The architecture uses a **schema-less** approach for the data of these events, relying on the power of the **PostgreSQL JSONB** data type so that each customer can store whatever properties they want without you having to modify the database.

## 2. Origin: Frontend (the 4kb Script)
The script supports two ways of capturing events: Programmatic (for developers) and Declarative (HTML, no-code).

### A. Declarative Method (HTML)
Ideal so the user does not have to touch JavaScript. The script listens for clicks automatically.

```html
<button 
  data-tempoo-goal="add_to_cart" 
  data-tempoo-goal-product="Nike Sneakers" 
  data-tempoo-goal-price="99.99"
  data-tempoo-goal-currency="EUR"
>
  Buy Now
</button>
```

**Script logic:**
- Detects the click on `data-tempoo-goal`.
- Extracts the event name: `add_to_cart`.
- Iterates over the `data-tempoo-goal-*` attributes and builds a JSON object:

```json
{
  "product": "Nike Sneakers",
  "price": "99.99",
  "currency": "EUR"
}
```

### B. Programmatic Method (JS)
For more complex integrations (e.g. after completing a payment or an AJAX sign-up).

```javascript
// The developer calls your public API
window.tempoo.track('subscription_success', { 
    plan: 'pro', 
    annual: true 
});
```

## 3. Transport: JSON Payload
When the script sends the data to the Ingestor (Go), the JSON has this standardised structure. Note how the custom data is encapsulated in the `data` field.

```json
POST /v1/event
{
  "site_id": "customer-uuid",
  "event_type": "custom",       // Fixed type
  "event_name": "add_to_cart",  // The event name
  "url": "https://shop.com/product/sneakers",
  "data": {                     // The flexible part goes here (JSONB)
    "product": "Nike Sneakers",
    "price": "99.99",
    "currency": "EUR"
  }
}
```

## 4. Ingestion: Go Service
The Go service must be able to receive any structure inside `data`. We will use `map[string]interface{}` for flexibility, or `json.RawMessage`.

**Go struct:**

```go
type AnalyticsEvent struct {
    SiteID    string                 `json:"site_id"`
    EventType string                 `json:"event_type"` // 'pageview', 'custom'
    EventName string                 `json:"event_name"` // 'add_to_cart'
    Data      map[string]interface{} `json:"data"`       // Flexible data
    // ... other fields (url, etc)
}
```

**Processing:**
1. Go receives the JSON.
2. It validates the SiteID.
3. It serializes the `Data` map to a safe JSON string to insert it into the database.
4. It adds the event to the bulk-insert buffer.

## 5. Storage: PostgreSQL (Schema)
This is the key to performance. We will use a JSONB column called `properties`.

**Table structure (events)**

| Column | Type | Description |
| :--- | :--- | :--- |
| timestamp | TIMESTAMPTZ | Exact date and time. |
| site_id | UUID | Site ID (part of the Primary Key). |
| event_type | LowCardinality(String) | 'pageview', 'custom', 'click'. |
| event_name | VARCHAR(255) | The main name ('add_to_cart'). |
| properties | JSONB | The container for the variable data. |

**Creation SQL (optimised)**

```sql
CREATE TABLE events (
    timestamp TIMESTAMPTZ NOT NULL,
    site_id UUID NOT NULL,
    event_type VARCHAR(50),
    event_name VARCHAR(255),
    properties JSONB DEFAULT '{}'::jsonb, -- The "data" object is stored here
    
    -- Key indexes for speed
    PRIMARY KEY (site_id, timestamp) 
);

-- GIN index to search INSIDE the JSON quickly
CREATE INDEX idx_events_properties ON events USING GIN (properties);
```

## 6. Consumption: Queries from .NET (Dashboard)
Thanks to the JSONB type, .NET can run very powerful SQL queries for the user's dashboard.

**Example 1: Count how many times an event occurred**
The user asks: "How many 'add_to_cart' events were there today?"

```sql
SELECT COUNT(*) 
FROM events 
WHERE site_id = @SiteId 
  AND event_name = 'add_to_cart'
  AND timestamp > NOW() - INTERVAL '24 hours';
```

**Example 2: Filter by an inner property (JSON)**
The user asks: "How many sneakers were added to the cart?"
Note: We use Postgres' `->>` operator to read inside the JSON.

```sql
SELECT COUNT(*) 
FROM events 
WHERE site_id = @SiteId 
  AND event_name = 'add_to_cart'
  AND properties->>'product' LIKE '%Sneakers%'; -- Search inside the JSON
```

**Example 3: Compute numeric metrics**
The user asks: "What is the total value of abandoned carts?"

```sql
SELECT SUM(CAST(properties->>'price' AS DECIMAL)) 
FROM events 
WHERE site_id = @SiteId 
  AND event_name = 'add_to_cart';
```

## 7. Implementation Summary
- **Script:** Collects `data-tempoo-goal-*` attributes and puts them in an object.
- **Go API:** Accepts a generic `data` field in the body.
- **Postgres:** Stores that `data` field in a JSONB column.
- **Dashboard:** Lets the user filter using JSON keys (Postgres does this natively and quickly thanks to the GIN index).
