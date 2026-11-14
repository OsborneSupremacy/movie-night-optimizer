# Sending only. Nothing here receives mail, so there is no MX on the site's name and no receipt
# rule: the account's one active receipt rule set belongs to another repository, and this
# application has no reason to touch it.
#
# The account is already out of the SES sandbox in this region, and that is a property of the
# account, so this identity can send to anybody as soon as it verifies.
resource "aws_ses_domain_identity" "movienight" {
  domain = local.site_domain
}

resource "aws_route53_record" "ses_verification" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "_amazonses.${local.site_domain}"
  type    = "TXT"
  ttl     = 600
  records = [aws_ses_domain_identity.movienight.verification_token]
}

resource "aws_ses_domain_identity_verification" "movienight" {
  domain = aws_ses_domain_identity.movienight.id

  depends_on = [aws_route53_record.ses_verification]
}

resource "aws_ses_domain_dkim" "movienight" {
  domain = aws_ses_domain_identity.movienight.domain
}

resource "aws_route53_record" "dkim" {
  count   = 3
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "${element(aws_ses_domain_dkim.movienight.dkim_tokens, count.index)}._domainkey.${local.site_domain}"
  type    = "CNAME"
  ttl     = 600
  records = ["${element(aws_ses_domain_dkim.movienight.dkim_tokens, count.index)}.dkim.amazonses.com"]
}

# A custom MAIL FROM domain so SPF aligns for DMARC as well as DKIM.
resource "aws_ses_domain_mail_from" "movienight" {
  domain                 = aws_ses_domain_identity.movienight.domain
  mail_from_domain       = "bounce.${local.site_domain}"
  behavior_on_mx_failure = "UseDefaultValue"
}

resource "aws_route53_record" "mail_from_mx" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = aws_ses_domain_mail_from.movienight.mail_from_domain
  type    = "MX"
  ttl     = 600
  records = ["10 feedback-smtp.${data.aws_region.current.region}.amazonses.com"]
}

resource "aws_route53_record" "mail_from_spf" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = aws_ses_domain_mail_from.movienight.mail_from_domain
  type    = "TXT"
  ttl     = 600
  records = ["v=spf1 include:amazonses.com -all"]
}

# No rua: there is no inbox on this name to send aggregate reports to.
resource "aws_route53_record" "dmarc" {
  zone_id = data.aws_route53_zone.main.zone_id
  name    = "_dmarc.${local.site_domain}"
  type    = "TXT"
  ttl     = 600
  records = ["v=DMARC1; p=quarantine"]
}
