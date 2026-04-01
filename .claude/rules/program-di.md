---
paths:
  - "Server/Program.cs"
---

# Program.cs / DI rules

- Make the smallest possible DI/config edit set.
- Preserve unrelated service registrations.
- Do not remove existing registrations unless they directly conflict with the requested Recursor work.
- Register Recursor services with lifetimes that match the architecture spec.
- In-memory repositories for active session state and sim catalog should remain singleton in the basic slice.
- ADX service registrations should follow the Recursor ADX plan.
- Before changing `Program.cs`, explain the exact registrations being added, removed, or changed.