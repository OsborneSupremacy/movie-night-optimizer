# CloudFront names are unique per account, not per distribution, so these carry the
# application's name: "frontend-oac" and "frontend-spa-router" already belong to another one.
resource "aws_cloudfront_origin_access_control" "frontend" {
  name                              = "movienight-frontend-oac"
  description                       = "Origin Access Control for the Movie Night Optimizer frontend bucket"
  origin_access_control_origin_type = "s3"
  signing_behavior                  = "always"
  signing_protocol                  = "sigv4"
}

resource "aws_cloudfront_function" "spa_router" {
  name    = "movienight-spa-router"
  runtime = "cloudfront-js-2.0"
  comment = "Rewrites SPA route paths to /index.html and leaves file paths to 404"
  publish = true
  code    = file("${path.module}/functions/spa-router.js")
}

# The same headers on the site and the API. The content security policy allows only this origin,
# plus TMDB's image host for posters: the build has no inline scripts, the API is same-origin, and
# the page loads nothing else from anywhere else. no-referrer matches the meta tag in index.html; the token is in the URL fragment, which no
# Referer carries anyway, but nothing about a night needs to leave with a click.
resource "aws_cloudfront_response_headers_policy" "security" {
  name    = "movienight-security-headers"
  comment = "Security headers for Movie Night Optimizer"

  security_headers_config {
    strict_transport_security {
      access_control_max_age_sec = 31536000
      include_subdomains         = false
      override                   = true
    }

    content_type_options {
      override = true
    }

    frame_options {
      frame_option = "DENY"
      override     = true
    }

    referrer_policy {
      referrer_policy = "no-referrer"
      override        = true
    }

    content_security_policy {
      content_security_policy = join("; ", [
        "default-src 'self'",
        "script-src 'self'",
        "style-src 'self'",
        "img-src 'self' data: https://image.tmdb.org",
        "connect-src 'self'",
        "object-src 'none'",
        "base-uri 'self'",
        "form-action 'self'",
        "frame-ancestors 'none'",
      ])
      override = true
    }
  }
}

locals {
  s3_origin_id  = "S3-${aws_s3_bucket.frontend.id}"
  api_origin_id = "API-${aws_apigatewayv2_api.movienight.id}"
}

resource "aws_cloudfront_distribution" "site" {
  enabled             = true
  is_ipv6_enabled     = true
  default_root_object = "index.html"
  price_class         = "PriceClass_100"
  aliases             = [local.site_domain]

  origin {
    domain_name              = aws_s3_bucket.frontend.bucket_regional_domain_name
    origin_id                = local.s3_origin_id
    origin_access_control_id = aws_cloudfront_origin_access_control.frontend.id
  }

  origin {
    # api_endpoint is a URL, and an origin wants the bare host name.
    domain_name = replace(aws_apigatewayv2_api.movienight.api_endpoint, "https://", "")
    origin_id   = local.api_origin_id

    custom_origin_config {
      http_port              = 80
      https_port             = 443
      origin_protocol_policy = "https-only"
      origin_ssl_protocols   = ["TLSv1.2"]
    }
  }

  # The site.
  default_cache_behavior {
    allowed_methods            = ["GET", "HEAD", "OPTIONS"]
    cached_methods             = ["GET", "HEAD"]
    target_origin_id           = local.s3_origin_id
    viewer_protocol_policy     = "redirect-to-https"
    compress                   = true
    cache_policy_id            = data.aws_cloudfront_cache_policy.caching_optimized.id
    response_headers_policy_id = aws_cloudfront_response_headers_policy.security.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.spa_router.arn
    }
  }

  # The API. Never cached, and the path reaches API Gateway unchanged, which is why every route
  # key there starts with /api.
  ordered_cache_behavior {
    path_pattern               = "/api/*"
    allowed_methods            = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods             = ["GET", "HEAD"]
    target_origin_id           = local.api_origin_id
    viewer_protocol_policy     = "https-only"
    compress                   = true
    cache_policy_id            = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id   = data.aws_cloudfront_origin_request_policy.all_viewer_except_host_header.id
    response_headers_policy_id = aws_cloudfront_response_headers_policy.security.id
  }

  restrictions {
    geo_restriction {
      restriction_type = "none"
    }
  }

  viewer_certificate {
    acm_certificate_arn      = aws_acm_certificate.site.arn
    ssl_support_method       = "sni-only"
    minimum_protocol_version = "TLSv1.2_2021"
  }

  tags = {
    Name = "movienight-distribution"
  }

  depends_on = [aws_acm_certificate_validation.site]
}
