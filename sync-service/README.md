# Invite-code sync service

Production: https://treasure-ledger-link.mlitw002.chatgpt.site

These files mirror the deployed API and D1 schema. The complete deployed application source is preserved in the associated Sites source repository, commit `21e933ea376fc94a4c0611998d233533045bbce0`. `route.ts` is a Vinext/Next route at `app/api/v1/[...path]/route.ts`, using the Cloudflare `env.DB` D1 binding. Apply schema.sql when provisioning an equivalent database. The Windows client endpoint is `LinkClient.ServiceUrl`.

Create/join/sync replace revisioned snapshots; counts are never accumulated from repeated requests. Invite codes authorize joining; a distinct per-installation proof authorizes subsequent updates. Stored credentials are SHA-256 hashes. Group creators have a separate admin capability for removing members or deleting a group. Removing a member rotates the invite code. No public endpoint lists groups, members or counts. Health exposes only service/protocol status. HTTP bodies are bounded and D1 queries use bound parameters.

Run `python scripts/test_link_service.py` from the main repository to exercise temporary synthetic members and verify cleanup. It makes no use of real player data and never prints credentials. Group creation is rate limited; do not use this integration test as a frequent health probe.
