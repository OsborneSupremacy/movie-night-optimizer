# The TMDB credentials. Terraform makes the secret and a first version with empty values, so the
# console shows the two keys ready to fill in; the real values are entered by hand there and never
# pass through Terraform, its plan or its state.
#
# ignore_changes keeps a later apply from putting the empty values back over the real ones.
#
# Nothing reads this yet. When a feature does, the Lambda's role gets secretsmanager:GetSecretValue
# on this secret's ARN, and nothing broader.
resource "aws_secretsmanager_secret" "tmdb" {
  name        = "movienight/tmdb"
  description = "TMDB API credentials for Movie Night Optimizer: apiReadAccessToken and apiKey"

  # A mistaken destroy can be undone for a week, rather than the default month during which the
  # name can't be reused.
  recovery_window_in_days = 7
}

resource "aws_secretsmanager_secret_version" "tmdb_placeholder" {
  secret_id = aws_secretsmanager_secret.tmdb.id

  secret_string = jsonencode({
    apiReadAccessToken = ""
    apiKey             = ""
  })

  lifecycle {
    ignore_changes = [secret_string]
  }
}
