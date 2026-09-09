# Demo walkthrough

A concise walkthrough of one merchant application from start through the normalized
review payload. Two ways to run it — the frontend (visual) or `curl` (text-only,
identical underlying calls) — both hit the same real backend.

## Setup (once)

```bash
# Terminal 1 -- backend, in-memory (fastest; see README "Frontend" for the
# MinIO/DynamoDB-Local setup if you want real document storage in the demo too)
PERSISTENCE_PROVIDER=inmemory AI_PROVIDER=mock dotnet run --project src/Gweb.Api
# -> listening on http://localhost:5243

# Terminal 2 -- frontend
cd frontend && npm install && npm run dev
# -> http://localhost:5173, proxies /v1 to the backend above
```

## Walkthrough: frontend

1. Open `http://localhost:5173` — the landing page. Click **Start a new application**.
2. **Applicant** — fill in name, DOB, address, email/phone, role, a government ID
   (only the last 4 digits are ever stored), and check the consent box. Continue.
3. **Business** — legal name, entity type, registration identifier (EIN/UBI — masked
   the same way), addresses, a business description (this feeds MCC classification —
   try "A neighborhood grocery store selling fresh produce and packaged foods"),
   volume profile, and settlement bank account (masked to last4). Continue.
4. **Documents** — upload any real PDF/JPG/PNG for the three required types
   (Government ID, Business Registration, Bank Evidence). Real upload progress, real
   pre-signed-URL flow. Continue once all three show **Received**.
5. **Classification** — click **Get MCC suggestion**. For the grocery description
   above, expect `5411 — Grocery Stores, Supermarkets` at 90% confidence, with two
   lower-ranked alternatives. Confirm it, or use the manual search box ("Not right?
   search for a code yourself") to pick a different real catalog code by hand.
6. **Rate & business evaluation** — click **Run evaluation**. With no processing
   statement uploaded, this still runs deterministic risk-signal detection (e.g.
   "No beneficial ownership information has been captured") and reports it, citing
   the exact field each signal came from.
7. **Review & submit** — a masked recap of everything entered, document status,
   the confirmed MCC, and the evaluation summary. Click **Submit for review** —
   a distinct success animation confirms the application is now locked, with a
   `Ready for manual review` badge. No `Approved` state exists anywhere to reach.

## Walkthrough: `curl` (identical calls, text-only)

```bash
BASE=http://localhost:5243

# 1. Create
ID=$(curl -s -X POST $BASE/v1/applications | grep -o '"id":"[^"]*"' | cut -d'"' -f4)
echo "Application: $ID"

# 2. Applicant
curl -s -X PATCH $BASE/v1/applications/$ID/applicant -H "Content-Type: application/json" -d '{
  "legalFirstName":"Jane","legalLastName":"Doe","dateOfBirth":"1985-01-15",
  "residentialAddress":{"line1":"1 Main St","city":"Springfield","state":"IL","postalCode":"62701","country":"US"},
  "email":"jane@freshvalleygrocers.example","phone":"+15551002000","roleTitle":"CEO",
  "governmentId":{"type":"Passport","number":"X1234567"},"consentVersion":"v1.0"
}' | head -c 200; echo

# 3. Business (feeds MCC classification below)
curl -s -X PATCH $BASE/v1/applications/$ID/business -H "Content-Type: application/json" -d '{
  "legalBusinessName":"Fresh Valley Grocers LLC","entityType":"Llc","formationCountry":"US",
  "registrationIdentifier":{"type":"Ein","value":"123456789"},
  "registeredAddress":{"line1":"100 Market St","city":"Springfield","state":"IL","postalCode":"62701","country":"US"},
  "operatingAddress":{"line1":"100 Market St","city":"Springfield","state":"IL","postalCode":"62701","country":"US"},
  "businessDescription":"A neighborhood grocery store selling fresh produce, dairy, and packaged foods.",
  "businessStartDate":"2020-03-01",
  "volumeProfile":{"expectedAnnualCardVolume":500000,"averageTicket":35,"highestTicket":150,"monthlyTransactionCount":2000,"cardPresentPercentage":70,"ecommercePercentage":30},
  "settlementBankAccount":{"accountHolder":"Fresh Valley Grocers LLC","bankName":"First National Bank","accountNumber":"000123456789","statementDate":"2026-08-01"}
}' | head -c 200; echo

# 4. Submit now -- correctly blocked, no documents yet
curl -s -X POST $BASE/v1/applications/$ID/submit
echo

# 5. Documents -- presign, "upload" (see README for the real MinIO flow), complete
for TYPE in GovernmentId BusinessRegistration BankEvidence; do
  echo "== $TYPE =="
  PRESIGN=$(curl -s -X POST $BASE/v1/applications/$ID/documents/presign -H "Content-Type: application/json" -d "{
    \"type\":\"$TYPE\",\"originalFilename\":\"$TYPE.pdf\",\"contentType\":\"application/pdf\",
    \"declaredSizeBytes\":20,\"declaredChecksumSha256Base64\":\"ZGVjbGFyZWQ=\"
  }")
  echo "$PRESIGN" | head -c 150; echo
  # In-memory mode: this demo doesn't actually land bytes in S3, so `complete` will
  # correctly report a validation failure here -- see README "Frontend" > MinIO setup
  # for the real end-to-end upload path this same call goes through for real.
done

# 6. Classify (works once business description is set, regardless of documents)
curl -s -X POST $BASE/v1/applications/$ID/classify | head -c 400; echo

# 7. Evaluate
curl -s -X POST $BASE/v1/applications/$ID/evaluate -H "Content-Type: application/json" -d '{}' | head -c 400; echo

# 8. Full state, any time
curl -s $BASE/v1/applications/$ID | head -c 400; echo
```

For the real, byte-for-byte document upload (not the placeholder shown in step 5
above), see README "Frontend" for the MinIO + DynamoDB Local setup — that is the
configuration this session actually used to verify a complete, real submission
end-to-end (see `docs/adr/0010-frontend-architecture.md`).

## What "review payload" means here

A successful `POST /v1/applications/{id}/submit` returns exactly the brief's "normalized
record for downstream mapping": masked applicant, masked business, every document's
status, the confirmed MCC classification, and the evaluation summary — one response,
composed from the same masked types every other endpoint already returns, so nothing
about masking can drift between "normal" responses and this one. See
`docs/openapi.yaml`'s `SubmitResponse` schema for the exact shape.
