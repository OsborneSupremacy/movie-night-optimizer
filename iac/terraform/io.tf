# inputs
variable "live_mode" {
  description = "Whether mail goes to the address it is addressed to. When false it is diverted to test_recipient, with \"[TEST for <address>]\" at the start of the subject."
  type        = bool
  default     = true
}

variable "test_recipient" {
  description = "Where mail goes when live_mode is false. Left empty, nothing is sent at all outside live mode."
  type        = string
  default     = ""
}

# outputs
output "site_url" {
  value = "https://${local.site_domain}"
}

output "frontend_bucket" {
  value = aws_s3_bucket.frontend.bucket
}

output "cloudfront_distribution_id" {
  value = aws_cloudfront_distribution.site.id
}

output "api_endpoint" {
  description = "The API's own address. Useful for telling an API problem from a CloudFront one."
  value       = aws_apigatewayv2_api.movienight.api_endpoint
}

output "tmdb_secret_name" {
  description = "Where the TMDB credentials are kept. Fill in its values in the Secrets Manager console."
  value       = aws_secretsmanager_secret.tmdb.name
}
