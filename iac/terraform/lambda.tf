resource "aws_cloudwatch_log_group" "movienight" {
  name              = "/aws/lambda/movienight"
  retention_in_days = 14
}

resource "aws_lambda_function" "movienight" {
  function_name = "movienight"
  description   = "Router for every Movie Night Optimizer API route"
  handler       = "MovieNight.Library::MovieNight.Library.Handlers.Router::FunctionHandler"
  runtime       = "dotnet10"
  architectures = ["arm64"]
  # Lambda scales CPU with memory, and 1769 MB is one full vCPU. This is a cold start setting:
  # the first request after an idle spell took 1.76 s in the function at 512 MB and 0.73 s here.
  # Warm requests take milliseconds either way, so at this traffic the cost is pennies.
  memory_size = 1769
  # Below the HTTP API's 30 second integration ceiling, so the function gives up first and a
  # failure the caller sees is a failure that happened.
  timeout          = 28
  filename         = local.publish_zip_path
  source_code_hash = filebase64sha256(local.publish_zip_path)
  role             = aws_iam_role.movienight_exec_role.arn

  environment {
    variables = merge(
      {
        TABLE_NAME     = aws_dynamodb_table.movienight.name
        SITE_URL       = "https://${local.site_domain}"
        SENDER_ADDRESS = local.sender_address
        LIVE_MODE      = tostring(var.live_mode)
      },
      var.test_recipient == "" ? {} : { TEST_RECIPIENT = var.test_recipient }
    )
  }

  depends_on = [aws_cloudwatch_log_group.movienight]
}

resource "aws_iam_role" "movienight_exec_role" {
  name = "movienight-exec-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Principal = {
          Service = "lambda.amazonaws.com"
        }
        Effect = "Allow"
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "movienight_exec_role_lambda_basic_execution" {
  role       = aws_iam_role.movienight_exec_role.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AWSLambdaBasicExecutionRole"
}

# Scan is withheld deliberately: every read is of one night by its key. A transactional write
# needs no action of its own, only the actions it is made of: PutItem and UpdateItem, and
# ConditionCheckItem for the check that a night is still in the phase a write belongs to.
resource "aws_iam_role_policy" "movienight_dynamodb_policy" {
  name = "movienight-dynamodb-policy"
  role = aws_iam_role.movienight_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "dynamodb:GetItem",
          "dynamodb:PutItem",
          "dynamodb:UpdateItem",
          "dynamodb:ConditionCheckItem",
          "dynamodb:Query"
        ]
        Resource = [aws_dynamodb_table.movienight.arn]
      }
    ]
  })
}

# Sending is allowed from this application's identity and no other in the account.
resource "aws_iam_role_policy" "movienight_ses_policy" {
  name = "movienight-ses-policy"
  role = aws_iam_role.movienight_exec_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "ses:SendEmail",
          "ses:SendRawEmail"
        ]
        Resource = [aws_ses_domain_identity.movienight.arn]
      }
    ]
  })
}
