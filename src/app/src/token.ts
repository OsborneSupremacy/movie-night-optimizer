/**
 * The token from a link like /night/{nightId}#t={token}.
 *
 * It travels in the fragment because a fragment is never sent to a server, so it stays out of
 * CloudFront's logs and out of any Referer header.
 */
export function readToken(): string {
  return new URLSearchParams(window.location.hash.replace(/^#/, '')).get('t') ?? ''
}
