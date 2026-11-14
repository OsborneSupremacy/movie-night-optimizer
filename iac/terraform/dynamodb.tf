# One partition per movie night: a META item, and an item per participant holding their
# suggestion and ranking. See NightProvider for the shape.
resource "aws_dynamodb_table" "movienight" {
  name         = "movienight"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "PK"
  range_key    = "SK"

  attribute {
    name = "PK"
    type = "S"
  }

  attribute {
    name = "SK"
    type = "S"
  }

  ttl {
    attribute_name = "ttl"
    enabled        = true
  }
}
