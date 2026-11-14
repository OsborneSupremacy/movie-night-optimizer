# Movie Night Optimizer

How to work in this repository. The README says what the app is and how it is built; this file is the conventions and practices that the code alone doesn't make obvious.

## Reference project

The same author's production app, `names-out-of-a-hat-api` (checked out beside this repository, at `../names-out-of-a-hat-api`), is the model for structure, style and Terraform patterns. Read its README's "Decisions" section and `.github/instructions/net.instructions.md`, and look there before inventing a pattern.

Deliberately left out of this project, though the reference project has them: DSQL and EF Core, a REST API with a module per endpoint, magic-link sign-in and an authorizer, SQS fan-out, delivery events, schedulers, WAF, an origin-verify header, X-Ray and Powertools, alarms, and a stage environment.

## Code conventions

Follow these in new code.

- No nulls: don't return, assign or store null. Convert a null from outside (a request body, DynamoDB, TMDB's JSON) to an empty string, empty collection or other default as soon as it is seen. Records have no nullable properties, except the shapes of outside JSON, which are turned into defaults before anything leaves the class that reads them.
- `.ConfigureAwait(false)` everywhere in the library except the top-level handler. Never in tests.
- Request and response records go in `Messaging`, not tuples, for anything beyond a trivial parameter list.
- LINQ method syntax only.
- Services are `internal`, marked `[UsedImplicitly]`, with constructor null checks.
- Comments explain why a thing is the way it is, not what the line does.
- Tests use xunit, FluentAssertions and NSubstitute, and are named `Method_Condition_Outcome`. A substitute `IAmazonDynamoDB` answers `GetItemAsync` with null unless told otherwise, which the real client never does.

## Rules the design depends on

- GET reads, POST acts. Mail scanners follow every link in an email, so opening a link must never change anything.
- Every write to a night is conditional on its phase, and the phase moves on only from the phase it was read in. Keep it that way, or two people finishing at once will both send the next round of email.
- Only the hash of a participant's token is stored, never the token.
- All mail leaves through `EmailSender`, which checks the do-not-email list. Never send mail any other way.
- TMDB is an enhancement, never a dependency. Anything that goes wrong with it means saving the suggestion as typed.
- `GET /api/movies/search` is public and cached at CloudFront for a day, keyed on `q` alone, so the same answer goes to everybody. Never put anything about a night or a person in it, and mark a failure `no-store` so it isn't cached.
- The route list is written in two places that must agree: `local.api_routes` in `iac/terraform/locals.tf` and `ServiceProviderBuilder.AddBusinessServices`. The C# keys are the route key lower-cased.

## Working practices

- One branch per change, pushed so that CI runs; merge into `main` with a fast-forward once CI passes.
- Run `dotnet test` and `npm run build` (in `src/app`) before deploying. The Lambda zip has to exist before `terraform plan`; `./scripts/build-lambdas.sh` builds it.
- Deploy with a saved plan: build the zip, `terraform plan -out`, read what it changes, then apply that plan. Then sync `src/app/dist` to the S3 bucket and invalidate CloudFront. `./scripts/deploy.sh` does all of this interactively. AWS credentials come from the `ben` profile (`aws sso login --profile ben`).
- `live_mode` defaults to true, so a plain deploy sends real mail. To divert every email to one inbox, pass `-var live_mode=false -var test_recipient=...` (`deploy.sh` passes its arguments through to `terraform apply`).
- There is no local API: `npm run dev` proxies `/api` to the deployed site, so it reads and writes real data and sends real mail.
- For live testing, use addresses at `osbornesupremacy.com`, which all reach the owner. Never use made-up domains such as `example.com` in live mode: the mail bounces and hurts the shared SES account's reputation. Rehearsals count against the limit of three nights per organizer address per day.
- Never create or activate an SES receipt rule set. The account's active set belongs to another repository; this app only sends.
