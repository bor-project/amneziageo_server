# HTTP upload capabilities

The authenticated version 2 `open` request can include `caps=1`. The response body is covered by the existing response MAC, including the request digest. The challenge format and authentication version remain unchanged.

An accepted request returns:

```json
{"version":1,"maxPostBytes":32768,"uploadWindow":4}
```

`uploadWindow` can be 1 when the extra connection budget is occupied. A repeated `open` for the same session returns the same limits. Changing negotiation or destination on an existing session returns a signed conflict.

An `open` without `caps` returns an empty body and selects the original 4096-byte, one-request upload mode. Older servers ignore `caps` and return that empty body. Clients use the original limits only for an authenticated empty response; invalid capabilities and invalid signatures terminate negotiation.

GET remains bodyless and carries a canonical Base64 `X-WG-Data` header with at most 4096 decoded bytes. POST can carry up to 32768 bytes in its body after negotiation. Datagram framing and the 2048-byte datagram limit remain unchanged. The C# relay and Rust listener both bound POST bodies at 32768 bytes, including requests without Content-Length; the session enforces its negotiated limit.

The upload acknowledgement is cumulative: it identifies the last contiguous batch submitted to UDP. The receiver holds at most the negotiated number of batches, rejects sequences beyond that window and rejects a pending duplicate with different data. Repeating a committed sequence does not submit its datagrams again. A failed UDP send terminates the session; partial progress is retained across a cancelled HTTP request.

The sender retains at most four outstanding batches. A verified cumulative acknowledgement can cancel the request or retry for a batch already delivered. Authentication, signature and certificate failures remain terminal.

Each listener admits at most 128 sessions and reserves 96 extra upload connection slots. A four-request session holds three slots until it is released or expires, allowing at most 32 simultaneous upgraded sessions. Other negotiated sessions use a one-request window with the POST body limit. The listener bounds HTTP connections at 416: two connections per session, 96 reserved upload connections and 64 connections for transient overlap and negotiation. Pending upload payload is bounded by four times the method's batch limit per upgraded session; the existing UDP queues and expiry remain unchanged.

These limits do not change TLS, destination restrictions, credential revocation or CDN requirements. CDN acceptance and performance require their own test matrix.
