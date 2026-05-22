---
applyTo: "**/*"
---

# Feature Completeness

Build success proves compilation. It proves nothing about behaviour. These standards exist because code that compiles cleanly can still be silently broken — callbacks wired but never triggered, UI elements that advertise features that do not exist, actions that return without doing anything or telling anyone why.

## Build ≠ Behaviour

> **"Build succeeded" is never a signal that work is complete.**

Compilation verifies types and syntax. It does not verify that a button click actually fires, that a registered handler is ever called, that a feature works as documented, or that a user-facing interaction produces the expected result.

When work cannot be verified at runtime — because there is no test harness, the environment is unavailable, or the UI cannot be exercised programmatically — say so explicitly. Do not present unverified behaviour as confirmed.

## Complete Implementation Chains

Every user-facing feature is a chain: a trigger connects to a handler connects to an action. All links in the chain must exist before the feature is considered done.

**Common failure modes:**

- A button, keyboard shortcut, or menu item is documented and visible — but no handler exists for it
- A handler is registered (event, callback, delegate) — but nothing ever calls it
- An action is implemented — but no trigger connects it to user input
- An interface or abstract method is declared — but left as a no-op or stub and never noticed

**The rule:** When you add any one part of a chain, locate and verify all the other parts. If they do not exist, either create them in the same change or explicitly flag them as missing.

Chains to audit in this codebase:
- UI label / key hint → key handler case → trigger method → registered callback → executing action
- Interface method / abstract member → all implementations → all call sites
- Event subscription → event firing → observable side effect

## Document–Code Consistency

Everything the user can read — UI hints, help text, comments, XML docs, error messages — is a promise about what the code does. Broken promises are bugs.

**Rule:** Any text visible to a user that describes a behaviour, shortcut, or feature must have a corresponding implementation. If the implementation does not exist, remove the text. If the text is wrong, fix it. Never leave documentation and code in disagreement.

Examples:
- A tooltip says "Press R to replay" → `ConsoleKey.R` must have a handler
- An XML doc says "Throws X when Y" → the code must throw X when Y
- A README says "Supports Z format" → Z must be supported

## No Silent Failures

When a user-triggered operation cannot proceed because a precondition is not met, always surface feedback. A method that silently returns accomplishes nothing except making the user believe the feature is broken.

**Rule:** Every early-return guard in a user-facing operation must explain why it returned. Use status messages, error indicators, or log output — whatever is appropriate for the context. Never return silently.

```csharp
// ❌ User sees nothing, concludes the feature is broken
if (selected is null) return;

// ✅ User sees "Select a row first"
if (selected is null) { ShowHint("Select a row first"); return; }
```

This applies equally to:
- User input handlers that silently drop input
- API endpoints that return empty 200s instead of 404s
- Background jobs that swallow exceptions
- Validation that fails without reporting what was invalid

## Carry-Forward Integrity

When extending existing code, do not assume it is complete. Scaffold, boilerplate, and copy-pasted patterns frequently have missing links — stubs, unimplemented handlers, wired-but-never-called callbacks. The fact that existing code was not flagged as broken does not mean it works.

**Rule:** When touching an existing feature, trace the full implementation chain — trigger → handler → action — and verify each link. Any gap you find is a pre-existing bug. Either fix it in the same change or leave an explicit note.

## Verification Checklist

Before marking any feature complete, confirm:

- [ ] Every user-visible description of a behaviour has a corresponding implementation
- [ ] Every registered handler/callback/listener is called by something
- [ ] Every trigger (button, shortcut, event) connects to a handler
- [ ] Every early return in a user-facing path provides user feedback
- [ ] If runtime behaviour could not be verified, that is stated explicitly
