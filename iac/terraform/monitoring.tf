# Alarms on the one Lambda behind the API, and email when one of them changes state.
#
# Only ALARM and OK matter. An alarm's own ok_actions would also fire on INSUFFICIENT_DATA -> OK,
# which every new alarm goes through, so the alarms carry no actions of their own. An EventBridge
# rule passes on just the transitions between ALARM and OK, and missing data counts as not
# breaching, so a quiet evening with no traffic is OK rather than INSUFFICIENT_DATA.

locals {
  alarm_email = "movienight@osbornesupremacy.com"

  lambda_alarm_dimensions = {
    FunctionName = aws_lambda_function.movienight.function_name
  }
}

# Unhandled exceptions and timeouts. The handlers turn every expected failure into an HTTP answer,
# so even one of these is worth a look.
resource "aws_cloudwatch_metric_alarm" "lambda_errors" {
  alarm_name          = "movienight-lambda-errors"
  alarm_description   = "The Movie Night Optimizer Lambda failed with an unhandled error or a timeout."
  namespace           = "AWS/Lambda"
  metric_name         = "Errors"
  dimensions          = local.lambda_alarm_dimensions
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
}

# Requests Lambda refused for lack of concurrency. At this traffic, only a sign of something odd.
resource "aws_cloudwatch_metric_alarm" "lambda_throttles" {
  alarm_name          = "movienight-lambda-throttles"
  alarm_description   = "The Movie Night Optimizer Lambda was throttled."
  namespace           = "AWS/Lambda"
  metric_name         = "Throttles"
  dimensions          = local.lambda_alarm_dimensions
  statistic           = "Sum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 1
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
}

# A request close to the 28 second timeout. Normal requests take milliseconds and a cold start
# about a second, so 10 seconds means something downstream (DynamoDB, SES, Secrets Manager) is
# struggling, before it turns into timeouts.
resource "aws_cloudwatch_metric_alarm" "lambda_duration" {
  alarm_name          = "movienight-lambda-slow"
  alarm_description   = "A Movie Night Optimizer Lambda request took 10 seconds or more."
  namespace           = "AWS/Lambda"
  metric_name         = "Duration"
  dimensions          = local.lambda_alarm_dimensions
  statistic           = "Maximum"
  period              = 300
  evaluation_periods  = 1
  threshold           = 10000
  comparison_operator = "GreaterThanOrEqualToThreshold"
  treat_missing_data  = "notBreaching"
}

resource "aws_sns_topic" "alarms" {
  name = "movienight-alarms"
}

# AWS emails this address a link to confirm the subscription; nothing arrives until it's clicked.
resource "aws_sns_topic_subscription" "alarms_email" {
  topic_arn = aws_sns_topic.alarms.arn
  protocol  = "email"
  endpoint  = local.alarm_email
}

resource "aws_cloudwatch_event_rule" "alarm_state_changes" {
  name        = "movienight-alarm-state-changes"
  description = "Movie Night Optimizer alarms moving between ALARM and OK, and nothing involving INSUFFICIENT_DATA"

  event_pattern = jsonencode({
    source      = ["aws.cloudwatch"]
    detail-type = ["CloudWatch Alarm State Change"]
    resources = [
      aws_cloudwatch_metric_alarm.lambda_errors.arn,
      aws_cloudwatch_metric_alarm.lambda_throttles.arn,
      aws_cloudwatch_metric_alarm.lambda_duration.arn,
    ]
    detail = {
      state         = { value = ["ALARM", "OK"] }
      previousState = { value = ["ALARM", "OK"] }
    }
  })
}

resource "aws_cloudwatch_event_target" "alarm_email" {
  rule = aws_cloudwatch_event_rule.alarm_state_changes.name
  arn  = aws_sns_topic.alarms.arn

  # A readable sentence rather than the raw event.
  input_transformer {
    input_paths = {
      alarm    = "$.detail.alarmName"
      state    = "$.detail.state.value"
      previous = "$.detail.previousState.value"
      reason   = "$.detail.state.reason"
      time     = "$.time"
    }
    input_template = "\"Movie Night Optimizer: <alarm> is now <state> (was <previous>) at <time>. <reason>\""
  }
}

# Lets EventBridge publish to the topic, for this rule only.
resource "aws_sns_topic_policy" "alarms" {
  arn = aws_sns_topic.alarms.arn

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "AllowAlarmStateChangeRule"
        Effect    = "Allow"
        Principal = { Service = "events.amazonaws.com" }
        Action    = "sns:Publish"
        Resource  = aws_sns_topic.alarms.arn
        Condition = {
          ArnEquals = { "aws:SourceArn" = aws_cloudwatch_event_rule.alarm_state_changes.arn }
        }
      }
    ]
  })
}
