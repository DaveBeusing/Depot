# Depot Authentication Security

## Password hashing

New Depot password hashes use PBKDF2-HMAC-SHA256 with a per-password 128-bit random salt, a 256-bit derived key and a current work factor of 600,000 iterations. The encoded format contains the algorithm and iteration count, allowing older hashes to remain verifiable while being identifiable for later upgrade.

The work factor must be benchmarked on supported production hardware before 1.0 and reviewed at least annually. It may be raised without invalidating existing hashes because verification uses the encoded iteration count.

## Password policy

New/changed passwords require 12-128 characters plus uppercase, lowercase, numeric and symbol characters, and may not contain a meaningful account-name component. Passwords are never logged or stored outside the password hash.

## Login throttling

Depot tracks failed attempts by normalized account key in process memory. Five failures inside a 15-minute window cause a 15-minute lockout. A successful authentication clears the failure state. This limits online guessing without creating a persistent denial-of-service flag in the user record.

The current limiter is per application process. A future multi-node/server authentication architecture must move throttling to a shared trusted store or identity provider.

## Identity roadmap

MFA and external identity providers (Microsoft Entra ID/OIDC and, where customer demand justifies it, SAML) are intentionally not coupled to the local authentication implementation during preview. Enterprise identity should be introduced behind an authentication-provider abstraction so local accounts remain usable for offline/recovery scenarios and external-provider policy can be centrally enforced.
