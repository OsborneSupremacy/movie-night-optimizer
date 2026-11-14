# Handoff: Movie Night Optimizer

Read this first, then `README.md`. This file is working notes for continuing the build and can
be deleted before submission.

## The goal

An entry for an AWS weekend challenge: build and ship a serverless app.

- **Deadline: Monday, November 16, 2026, 1:00 PM PT (3:00 PM Central).**
- The submission needs: the problem and how the app solves it, the services used and how they
  connect (an architecture diagram helps), at least one serverless AWS service, and a public
  demo video under three minutes showing the app running end to end.

The app: a family can't agree on a movie. An organizer enters everyone's email and a date.
Everyone is emailed a link to suggest one movie. When all suggestions are in, everyone is emailed
a link to rank the suggestions, except their own. When all rankings are in, everyone is emailed
the results.

## Decisions already made

Don't reopen these without a reason.

- **C# on .NET 10, arm64 Lambda.** One router Lambda dispatches on the HTTP API route key to a
  keyed `IApiGatewayHandler`.
- **DynamoDB only.** One partition per night (`NIGHT#{id}`): a `META` item and one
  `PARTICIPANT#{id}` item per person holding their suggestion and ranking. TTL of 30 days.
- **API Gateway HTTP API**, payload format 2.0, not a REST API.
- **One origin.** `movienight.osbornesupremacy.com` serves the site from S3 and the API under
  `/api/*`, both through one CloudFront distribution. No CORS, no API custom domain. Every route
  key therefore starts with `/api`.
- **Suggestions happen on a page, not by replying to the email.** There is no inbound mail.
- **No accounts.** Each participant gets a token in their link's URL fragment
  (`/night/{nightId}#t={token}`), sent to the API in the `X-Participant-Token` header. Only
  `SecretToken.Hash` of it is stored.
- **GET reads, POST acts.** Opening an emailed link must never change anything, because mail
  scanners follow links.
- **Borda count** scoring; ties go to more first-place votes. Nobody ranks their own suggestion.
- **Limits:** at most 8 people per night, API stage throttled to 5 requests a second.
- **Terraform**, one root in `iac/terraform`, state in the `bro-tfstate` bucket under
  `movienight/live`, region us-east-1. DNS is in Route 53 (`osbornesupremacy.com` zone).
- **SES is already out of the sandbox** in us-east-1.

## Reference project

`/Users/ben-osborne/Projects/names-out-of-a-hat-api` is the same author's production app and
the model for structure, style and Terraform patterns. Read its `README.md` "Decisions" section
and `.github/instructions/net.instructions.md`. Look there before inventing a pattern.

Deliberately left out of this project for the weekend: DSQL and EF Core, REST API with a module
per endpoint, magic-link sign-in and the authorizer, SQS fan-out, delivery events, schedulers,
WAF, the origin-verify header, X-Ray and Powertools, alarms, a stage environment.

## Current state

As of early Saturday, November 14.

`main` (`411e55e`) is pushed and has the whole night flow. Branch `feat/demo-polish`
(`4c54002`) is local only, not pushed or merged, and is what is deployed right now, in test mode.
It adds:

- Test-mode subjects that start with who the mail was for: `[TEST for one@example.com] Pick a
  movie...`. One inbox can then play the whole family for the demo.
- The night page checks for the next round every 15 seconds while you wait. Checked live: the
  page switched to ranking by itself after the last suggestion.
- `docs/architecture.svg` and `.png`. The README now shows the SVG in place of the Mermaid chart.
- `docs/SUBMISSION.md`: a draft write-up (problem, how it works, services, design decisions) and
  a timed demo script.

**Deployed in live mode** (since Saturday morning, for filming): mail goes to the real
addresses. To go back to test mode, where all mail goes to `movienight@osbornesupremacy.com`:

    AWS_PROFILE=ben ./scripts/deploy.sh -var live_mode=false -var test_recipient=movienight@osbornesupremacy.com

Running `deploy.sh` without those variables switches the app to live mail.

Tested: 79 unit tests, and 31 end-to-end checks against the deployed API (create, suggest,
rank, results, closing early, and the error cases). SES sent exactly the mail expected. The test
nights and their mail are in the table and the test inbox; the table's TTL deletes them in 30
days.

**Each inbox can start only 3 nights a day.** Rehearsals count, so use a different organizer
address for each take.

## What to do next, in order

1. Review the test emails, and `docs/SUBMISSION.md`.
2. Merge `feat/demo-polish` into `main` and push.
3. Decide whether to film in test mode or live, then film the demo using the script in
   `docs/SUBMISSION.md`.
4. Submit before Monday, November 16, 1:00 PM PT.

Stretch, only if everything above is done: merge duplicate suggestions (see the fairness note
in `BordaScorer`), TMDB lookups for posters, reminder emails.

## Gotchas

- **The route list is written in two places** that must agree: `local.api_routes` in
  `iac/terraform/locals.tf` and `ServiceProviderBuilder.AddBusinessServices`. The C# keys are
  the route key lower-cased, so `{nightId}` becomes `{nightid}` in the key, while
  `request.PathParameters` keeps the original casing (`nightId`).
- **CloudFront function and OAC names are unique per account.** `frontend-spa-router` and
  `frontend-oac` already exist for the other app; this one uses `movienight-` prefixes.
- **Do not create or activate an SES receipt rule set.** The account has one active set, owned
  by another repository. This app sends only.
- **POST /api/nights mails addresses a stranger typed**, from an SES account that other
  applications share. The per-organizer limit is not optional.
- **AWS SDK v4 leaves collections null** rather than empty in responses (`response.Items`).
- **The Lambda zip must exist before `terraform plan`**, because the source hash is read from
  it. `deploy.sh` builds it first.
- **There is no local API.** `npm run dev` proxies `/api` to the deployed site, so it reads and
  writes real data and sends real mail.
- `live_mode` defaults to true. Apply with `-var live_mode=false -var test_recipient=...` to
  divert mail while building.

## Conventions

From the reference project. Follow them in new code.

- No nulls: don't return, assign or store null. Convert a null from outside (request body,
  DynamoDB) to an empty string, empty collection or other default as soon as it is seen. Records
  have no nullable properties.
- `.ConfigureAwait(false)` everywhere in the library except the top-level handler. Never in
  tests.
- Request and response records in `Messaging`, not tuples, for anything beyond a trivial
  parameter list.
- LINQ method syntax only.
- Services are `internal`, marked `[UsedImplicitly]`, with constructor null checks.
- Comments explain why a thing is the way it is, not what the line does.
- Tests: xunit and FluentAssertions, named `Method_Condition_Outcome`.
