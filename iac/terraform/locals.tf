locals {
  # The site, the API (under /api) and the address mail is sent from all live on this one name.
  site_domain = "movienight.osbornesupremacy.com"

  sender_address = "donotreply@${local.site_domain}"

  publish_zip_path = "../../src/MovieNight.Library/bin/MovieNight.Library.zip"

  # Every route the API answers. The other place this list is written down is
  # ServiceProviderBuilder.AddBusinessServices, which keys a handler to each of these lower-cased.
  api_routes = [
    "GET /api/health",
    "POST /api/nights",
    "GET /api/nights/{nightId}",
    "POST /api/nights/{nightId}/suggestion",
    "POST /api/nights/{nightId}/ranking",
    "POST /api/nights/{nightId}/advance",
    "POST /api/nights/{nightId}/opt-out",
  ]
}
