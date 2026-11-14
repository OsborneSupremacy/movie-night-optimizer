provider "aws" {
  region = "us-east-1"

  default_tags {
    tags = {
      Environment = "live"
      Application = "movienight"
      ManagedBy   = "terraform"
    }
  }
}

terraform {
  required_providers {
    aws = {
      source = "hashicorp/aws"
    }
  }

  backend "s3" {
    bucket       = "bro-tfstate"
    use_lockfile = true
    key          = "movienight/live"
    region       = "us-east-1"
  }
}
