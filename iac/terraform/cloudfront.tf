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
    allowed_methods        = ["GET", "HEAD", "OPTIONS"]
    cached_methods         = ["GET", "HEAD"]
    target_origin_id       = local.s3_origin_id
    viewer_protocol_policy = "redirect-to-https"
    compress               = true
    cache_policy_id        = data.aws_cloudfront_cache_policy.caching_optimized.id

    function_association {
      event_type   = "viewer-request"
      function_arn = aws_cloudfront_function.spa_router.arn
    }
  }

  # The API. Never cached, and the path reaches API Gateway unchanged, which is why every route
  # key there starts with /api.
  ordered_cache_behavior {
    path_pattern             = "/api/*"
    allowed_methods          = ["DELETE", "GET", "HEAD", "OPTIONS", "PATCH", "POST", "PUT"]
    cached_methods           = ["GET", "HEAD"]
    target_origin_id         = local.api_origin_id
    viewer_protocol_policy   = "https-only"
    compress                 = true
    cache_policy_id          = data.aws_cloudfront_cache_policy.caching_disabled.id
    origin_request_policy_id = data.aws_cloudfront_origin_request_policy.all_viewer_except_host_header.id
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
