data "aws_caller_identity" "current" {}

data "aws_region" "current" {}

data "aws_route53_zone" "main" {
  name         = "osbornesupremacy.com"
  private_zone = false
}

# Looked up by name rather than written as ids, so nothing here depends on a remembered GUID.
data "aws_cloudfront_cache_policy" "caching_optimized" {
  name = "Managed-CachingOptimized"
}

data "aws_cloudfront_cache_policy" "caching_disabled" {
  name = "Managed-CachingDisabled"
}

# Forwards everything the browser sent except Host. API Gateway routes on Host, so passing the
# site's through would have it look for an API called movienight.osbornesupremacy.com and answer 403.
data "aws_cloudfront_origin_request_policy" "all_viewer_except_host_header" {
  name = "Managed-AllViewerExceptHostHeader"
}
