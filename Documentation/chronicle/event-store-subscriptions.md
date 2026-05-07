# Event Store Subscriptions

Event store subscriptions let one event store consume events produced in another event store. You configure subscriptions on a target event store and point each subscription to a source event store and a set of event types.

## list

Lists all event store subscriptions configured for the target event store.

```bash
cratis chronicle event-store-subscriptions list --event-store <TARGET_EVENT_STORE>
```

### Examples

List subscriptions for the `system` event store:

```bash
cratis chronicle event-store-subscriptions list --event-store system
```

List in plain format:

```bash
cratis chronicle event-store-subscriptions list --event-store system --output plain
```

## add

Adds an event store subscription to the target event store.

```bash
cratis chronicle event-store-subscriptions add <SUBSCRIPTION_ID> <SOURCE_EVENT_STORE> <EVENT_TYPES> --event-store <TARGET_EVENT_STORE>
```

### Arguments

| Argument | Description |
|---|---|
| `SUBSCRIPTION_ID` | Unique identifier for the subscription. |
| `SOURCE_EVENT_STORE` | Event store to subscribe from. |
| `EVENT_TYPES` | Comma-separated list of event type IDs to include. |

### Examples

Add a subscription from `default` to `system` for one event type:

```bash
cratis chronicle event-store-subscriptions add orders-from-default default MyCompany.Sales.OrderPlaced --event-store system
```

Add a subscription with multiple event types:

```bash
cratis chronicle event-store-subscriptions add sales-feed default MyCompany.Sales.OrderPlaced,MyCompany.Sales.OrderCancelled --event-store system
```

## remove

Removes an event store subscription from the target event store.

```bash
cratis chronicle event-store-subscriptions remove <SUBSCRIPTION_ID> --event-store <TARGET_EVENT_STORE>
```

The command prompts for confirmation before proceeding. Pass `--yes` to skip the prompt in automated workflows.

### Arguments

| Argument | Description |
|---|---|
| `SUBSCRIPTION_ID` | Identifier of the subscription to remove. Use `event-store-subscriptions list` to retrieve IDs. |

### Options

| Flag | Description |
|---|---|
| `-y, --yes` | Skip confirmation prompt. |

### Examples

Remove a subscription interactively:

```bash
cratis chronicle event-store-subscriptions remove orders-from-default --event-store system
```

Remove without confirmation:

```bash
cratis chronicle event-store-subscriptions remove orders-from-default --event-store system --yes
```
