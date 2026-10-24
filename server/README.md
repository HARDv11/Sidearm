# Sidearm post office

A small Cloudflare Worker that lets two Sidearm copies send each other notes and links. One inbox per Sidearm copy, each a SQLite Durable Object. It runs on Cloudflare's free plan; nobody has to keep a server running.

**It can't read anything.** Sidearm encrypts a message on the PC before sending it. The server only ever sees public keys (who may write to whom), ciphertext and times.

## How it works

- **Identity.** Each Sidearm copy makes a P-256 key pair and keeps the private key in `data\`. Its inbox id is the first 26 base64url characters of the SHA-256 of its raw public key. Nobody can claim someone else's inbox, because every request is signed.
- **Signed requests.** The signer sends its raw public key (65 bytes, base64url), the time in ms and an ECDSA P-256 / SHA-256 signature (raw r‖s, 64 bytes) over `METHOD\nPATH\nTIME\nhex(sha256(body))`. These go in the headers `X-Sidearm-Key`, `X-Sidearm-Time` and `X-Sidearm-Sig`, or in the query (`k`, `t`, `s`) for WebSocket clients that can't set headers. A signature is good for 5 minutes either way. The Worker checks it **before** it touches an inbox, so strangers cost an inbox nothing.
- **Friends.** Only keys on the owner's friend list may write. A stranger needs a one-time invite token from the owner (shared through Discord, Telegram and the like, inside the invite code). The invite turns the stranger's first message into a `request`, which the owner accepts by adding the key to the friend list.
- **Push, cheaply.** While Sidearm runs it keeps one WebSocket to its inbox. New messages are pushed straight away. Cloudflare doesn't bill outgoing messages, and a hibernating socket costs nothing while it's idle. `ping` is answered with `pong` by the runtime without waking the inbox. Drawing and holstering Sidearm doesn't reconnect, so how often someone clicks doesn't change the cost.
- **Offline.** Messages wait until the owner acks them, for up to 7 days.

## Endpoints

All under `/v1/inbox/<id>/`, all signed.

| Method and path | Who | Body | Does |
| --- | --- | --- | --- |
| `GET ws` | owner | | WebSocket. The server sends `{t:"hello", list}`, then `{t:"msg", msg}` as messages arrive. The client sends `ping`, `{t:"ack", ids}` or `{t:"fetch"}` |
| `GET messages` | owner | | `{list:[{id, from, kind, ct, at}]}`: the fallback when there's no socket |
| `POST ack` | owner | `{ids}` | Removes delivered messages |
| `PUT friends` | owner | `{friends:[key…]}` | Replaces the friend list (up to 64 keys) |
| `POST invites` | owner | `{token}` | A one-time invite, valid for 24 hours (up to 10 open at a time) |
| `POST send` | anyone signed | `{id, ct, invite?}` | Friends leave a `msg`; a valid invite leaves a `request`; anyone else gets 403 |
| `POST delete` | owner | `{}` | Deletes the inbox |

**Limits:**
- each sender can write 20 messages a minute and 300 a day to each inbox;
- `ct` (the ciphertext) can be up to 8 KB of base64url;
- an inbox holds up to 500 waiting messages;
- a request body can be up to 64 KB.

## Cost

On the free plan the limits are 100,000 Worker requests a day, plus Durable Object requests (WebSocket connects, and incoming socket messages at 20 to 1) and SQLite rows. A Sidearm copy that stays connected makes roughly 10–80 requests a day: reconnects after sleep or network changes, plus keep-alives. Draws and holsters add nothing. The paid plan is $5 a month and includes 10 million requests.

## Develop and deploy

```bash
cd server
npm install
npx wrangler dev --local          # a local Cloudflare runtime on :8787
node test/smoke.mjs http://127.0.0.1:8787
```

**Deploying** happens through GitHub Actions (`.github/workflows/deploy-post-office.yml`) on every push that changes `server/`, or by hand from the Actions tab. It needs two repository secrets: `CLOUDFLARE_API_TOKEN` (a token made from the "Edit Cloudflare Workers" template) and `CLOUDFLARE_ACCOUNT_ID`. After deploying, the workflow runs `test/smoke.mjs` against the live URL. That check creates two throwaway identities and deletes its test inbox when it's done.
