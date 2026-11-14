# An HTTP API with every route sent to the one router Lambda. Reached through the site's
# CloudFront distribution at /api/*, so the browser sees one origin and there is no CORS to
# configure. Its own execute-api address stays reachable; see the api_endpoint output.
resource "aws_apigatewayv2_api" "movienight" {
  name          = "movienight"
  description   = "Movie Night Optimizer API"
  protocol_type = "HTTP"
}

resource "aws_apigatewayv2_integration" "router" {
  api_id                 = aws_apigatewayv2_api.movienight.id
  integration_type       = "AWS_PROXY"
  integration_uri        = aws_lambda_function.movienight.invoke_arn
  payload_format_version = "2.0"
}

resource "aws_apigatewayv2_route" "routes" {
  for_each = toset(local.api_routes)

  api_id    = aws_apigatewayv2_api.movienight.id
  route_key = each.value
  target    = "integrations/${aws_apigatewayv2_integration.router.id}"
}

resource "aws_apigatewayv2_stage" "default" {
  api_id      = aws_apigatewayv2_api.movienight.id
  name        = "$default"
  auto_deploy = true

  # For each route on its own, across every caller: an HTTP API applies default route throttling
  # per route, so the autocomplete search can't use up what submitting a suggestion needs. Low on
  # purpose: a family choosing a film makes a handful of requests, and POST /api/nights sends mail.
  # It also caps autocomplete's calls to TMDB far below TMDB's own limit.
  default_route_settings {
    throttling_rate_limit  = 5
    throttling_burst_limit = 10
  }
}

resource "aws_lambda_permission" "movienight_allow_apigw_invoke" {
  statement_id  = "AllowExecutionFromAPIGateway-Invoke"
  action        = "lambda:InvokeFunction"
  function_name = aws_lambda_function.movienight.arn
  principal     = "apigateway.amazonaws.com"
  source_arn    = "${aws_apigatewayv2_api.movienight.execution_arn}/*/*"
}
