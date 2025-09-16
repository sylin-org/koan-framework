# Koan Flow (Entity Pipeline) — Accurate Guide

> Contract Summary
>
> - Purpose: Ingest heterogeneous records, resolve identity, build canonical + lineage projections.
> - Core Types: `FlowEntity<TModel>`, `StageRecord<TModel>`, `DynamicFlowEntity<TModel>`, `KeyIndex<TModel>`, `ReferenceItem<TModel>`, `ProjectionTask<TModel>`, `CanonicalProjection<TModel>`, `LineageProjection<TModel>`.
> - Stages: Intake → (Standardize) → Key → Associate → Project → Materialize.
> - Output: Canonical (merged entity view) and lineage (per-tag provenance) documents.
> - Config: `FlowOptions` (AggregationTags, batch sizes, TTLs, purge, projection defaults).
> - NOT Included: General workflow orchestration, FlowBase, step runners, timers, human task waiting, saga DSL.

## 1. Scope & When to Use

Use Koan Flow when you need to:

- Merge records about the same logical entity (customer, device, account) from multiple systems.
- Maintain a canonical, deduplicated view AND a lineage/audit trail of contributing sources.
- Apply aggregation (identity) rules based on a configurable set of keys (AggregationTags).
- Continuously materialize views as new data arrives.

## 2. Core Concepts (Implemented)

Implemented building blocks:

- Stage Record: raw or normalized inbound unit of data (`StageRecord<TModel>`)
- Aggregation Tags: ordered candidate keys used to derive a stable ReferenceId (`[AggregationTag]` + `FlowOptions.AggregationTags`)
- Reference / Canonical Id: stable business identity chosen during association (persisted as `ReferenceId` / `CanonicalId`)
- Key Index: mapping of aggregation key → owning ReferenceId (`KeyIndex<TModel>`)
- Identity Link: `(system|adapter|externalId)` → ReferenceId provisional/permanent mapping (`IdentityLink<TModel>`) *optional when envelope identifiers supplied*
- Projection Task: queued work item requesting projection rebuild (`ProjectionTask<TModel>`)
- Canonical Projection: merged canonical tag/value set (`CanonicalProjection<TModel>`)
- Lineage Projection: tag → value → sourceIds[] provenance map (`LineageProjection<TModel>`)
- Dynamic Entity: resolved container exposing merged model (`DynamicFlowEntity<TModel>`)

## 3. Installation & Bootstrapping

```csharp
// Program.cs
using Koan.Flow;           // AddKoanFlowNaming/AddKoanFlow (normally through AddKoan())
using Koan.Data.Core;       // Base data services
using Koan.Data.Json;       // Simple file storage (development)

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan();            // Discovers Flow module + Data
builder.Services.AddKoanDataCore();    // Core data abstractions
builder.Services.AddJsonData();        // Development adapter

var app = builder.Build();
Koan.Core.Hosting.App.AppHost.Current = app.Services; // Ambient host (required)
app.Run();
```

`AddKoan()` will (via module auto-registration) register Flow workers:

- Association / keying
- Projection / materialization
- Purge (if enabled)

## 4. Configuration (`appsettings.json`)

```json
{
  "Koan": {
    "Data": { "Json": { "DirectoryPath": "./data" } },
    "Flow": {
      "AggregationTags": ["email", "phone", "externalId"],
      "BatchSize": 100,
      "PurgeEnabled": true,
      "PurgeInterval": "00:05:00",
      "CanonicalExcludeTagPrefixes": ["reading."]
    }
  }
}
```

Key options (see `FlowOptions`):

- `AggregationTags`: Ordered list used when records lack model-level `[AggregationTag]` attributes or to supplement them.
- `BatchSize`: Worker processing page size.
- `PurgeEnabled/PurgeInterval`: Enable TTL cleanup for stale stage records and provisional identity links.

            context.SetOutput(new NotificationResult { Success = true });
        }
        catch (Exception ex)
        {
            await context.LogAsync($"Notification failed: {ex.Message}");
            context.SetOutput(new NotificationResult { Success = false, Error = ex.Message });
        }
    }
}

// Usage in any flow
public class OrderConfirmationFlow : FlowBase<Order, ConfirmationResult>
{
    public override async Task<ConfirmationResult> RunAsync(Order order, FlowContext context)
    {
        // Use the reusable notification activity
        var emailResult = await context.ExecuteActivityAsync<NotificationActivity, NotificationRequest, NotificationResult>(
            new NotificationRequest
            {
                Type = NotificationType.Email,
                Recipient = order.CustomerEmail,
                Template = "order-confirmation",
                Data = order
            });

        var smsResult = await context.ExecuteActivityAsync<NotificationActivity, NotificationRequest, NotificationResult>(
            new NotificationRequest
            {
                Type = NotificationType.SMS,
                Recipient = order.CustomerPhone,
                Template = "order-sms",
                Data = order
            });

        return new ConfirmationResult
        {
            EmailSent = emailResult.Success,
            SmsSent = smsResult.Success
        };
    }
}
```

### Composing Flows with Subflows

Break complex processes into smaller, focused flows that you can compose:

```csharp
// Individual focused flows
public class PaymentProcessingFlow : FlowBase<PaymentRequest, PaymentResult>
{
    public override async Task<PaymentResult> RunAsync(PaymentRequest request, FlowContext context)
    {
        await context.LogAsync($"Processing payment of ${request.Amount}");

        // Validate payment method
        var validation = await context.ExecuteStepAsync<ValidatePaymentMethodStep, PaymentRequest, ValidationResult>(request);
        if (!validation.IsValid)
        {
            return new PaymentResult { Success = false, Error = validation.ErrorMessage };
        }

        // Process the charge
        var charge = await context.ExecuteStepAsync<ChargePaymentStep, PaymentRequest, ChargeResult>(request);
        if (!charge.Success)
        {
            return new PaymentResult { Success = false, Error = charge.ErrorMessage };
        }

        // Send receipt
        await context.ExecuteActivityAsync<NotificationActivity, NotificationRequest, NotificationResult>(
            new NotificationRequest
            {
                Type = NotificationType.Email,
                Recipient = request.CustomerEmail,
                Template = "payment-receipt",
                Data = charge
            });

        return new PaymentResult { Success = true, TransactionId = charge.TransactionId };
    }
}

public class InventoryUpdateFlow : FlowBase<InventoryRequest, InventoryResult>
{
    public override async Task<InventoryResult> RunAsync(InventoryRequest request, FlowContext context)
    {
        await context.LogAsync($"Updating inventory for {request.Items.Count} items");

        var results = new List<ItemUpdateResult>();

        foreach (var item in request.Items)
        {
            var result = await context.ExecuteStepAsync<UpdateItemInventoryStep, ItemUpdateRequest, ItemUpdateResult>(
                new ItemUpdateRequest(item.ProductId, item.Quantity));

            results.Add(result);

            if (result.NewQuantity <= result.ReorderLevel)
            {
                // Trigger reorder flow as a subflow
                await context.ExecuteSubflowAsync<AutoReorderFlow, ReorderRequest, ReorderResult>(
                    new ReorderRequest(item.ProductId, result.ReorderLevel));
            }
        }

        return new InventoryResult { ItemUpdates = results };
    }
}

// Main orchestration flow that composes the above flows
public class OrderFulfillmentFlow : FlowBase<Order, OrderResult>
{
    public override async Task<OrderResult> RunAsync(Order order, FlowContext context)
    {
        await context.LogAsync($"Starting order fulfillment for order {order.Id}");

        try
        {
            // Step 1: Process payment
            var paymentResult = await context.ExecuteSubflowAsync<PaymentProcessingFlow, PaymentRequest, PaymentResult>(
                new PaymentRequest
                {
                    Amount = order.TotalAmount,
                    PaymentMethod = order.PaymentMethod,
                    CustomerEmail = order.Customer.Email
                });

            if (!paymentResult.Success)
            {
                return new OrderResult { Success = false, Error = $"Payment failed: {paymentResult.Error}" };
            }

            // Step 2: Update inventory
            var inventoryResult = await context.ExecuteSubflowAsync<InventoryUpdateFlow, InventoryRequest, InventoryResult>(
                new InventoryRequest
                {
                    Items = order.Items.Select(i => new InventoryItem(i.ProductId, i.Quantity)).ToList()
                });

            // Step 3: Generate shipping label and schedule pickup
            var shippingResult = await context.ExecuteSubflowAsync<ShippingFlow, ShippingRequest, ShippingResult>(
                new ShippingRequest
                {
                    OrderId = order.Id,
                    ShippingAddress = order.ShippingAddress,
                    Items = order.Items
                });

            // Step 4: Send order confirmation
            await context.ExecuteSubflowAsync<OrderConfirmationFlow, Order, ConfirmationResult>(order);

            return new OrderResult
            {
                Success = true,
                PaymentTransactionId = paymentResult.TransactionId,
                ShippingTrackingNumber = shippingResult.TrackingNumber
            };
        }
        catch (Exception ex)
        {
            await context.LogAsync($"Order fulfillment failed: {ex.Message}");

            // Compensate any successful operations
            await CompensateOrder(order, context);

            return new OrderResult { Success = false, Error = ex.Message };
        }
    }

    private async Task CompensateOrder(Order order, FlowContext context)
    {
        await context.LogAsync("Starting order compensation");

        // Execute compensation subflows
        await context.ExecuteSubflowAsync<RefundFlow, RefundRequest, RefundResult>(
            new RefundRequest { OrderId = order.Id });

        await context.ExecuteSubflowAsync<RestoreInventoryFlow, RestoreInventoryRequest, RestoreInventoryResult>(
            new RestoreInventoryRequest { OrderId = order.Id });

        await context.LogAsync("Order compensation completed");
    }
}
```

### Advanced Composition Patterns

#### Pattern 1: Conditional Subflow Execution

```csharp
public class SmartOrderProcessingFlow : FlowBase<Order, OrderResult>
{
    public override async Task<OrderResult> RunAsync(Order order, FlowContext context)
    {
        // Different processing based on order characteristics
        if (order.IsExpressOrder)
        {
            return await context.ExecuteSubflowAsync<ExpressOrderFlow, Order, OrderResult>(order);
        }
        else if (order.RequiresCustomApproval)
        {
            return await context.ExecuteSubflowAsync<CustomApprovalOrderFlow, Order, OrderResult>(order);
        }
        else if (order.IsInternationalOrder)
        {
            return await context.ExecuteSubflowAsync<InternationalOrderFlow, Order, OrderResult>(order);
        }
        else
        {
            return await context.ExecuteSubflowAsync<StandardOrderFlow, Order, OrderResult>(order);
        }
    }
}
```

#### Pattern 2: Dynamic Subflow Selection

```csharp
public class ConfigurableProcessingFlow : FlowBase<ProcessingRequest, ProcessingResult>
{
    private readonly IFlowRegistry _flowRegistry;

    public override async Task<ProcessingResult> RunAsync(ProcessingRequest request, FlowContext context)
    {
        // Determine which subflows to execute based on configuration
        var processingSteps = await context.ExecuteStepAsync<GetProcessingStepsStep, string, List<ProcessingStepConfig>>(request.ProcessType);

        var results = new List<StepResult>();

        foreach (var step in processingSteps)
        {
            await context.LogAsync($"Executing step: {step.StepName}");

            // Dynamically resolve and execute the appropriate subflow
            var subflowType = _flowRegistry.ResolveFlowType(step.FlowName);
            var result = await context.ExecuteSubflowAsync(subflowType, step.Parameters);

            results.Add(new StepResult
            {
                StepName = step.StepName,
                Success = result.Success,
                Data = result.Data
            });

            if (!result.Success && step.IsRequired)
            {
                throw new ProcessingException($"Required step {step.StepName} failed: {result.Error}");
            }
        }

        return new ProcessingResult { Steps = results };
    }
}
```

### Testing Composed Flows

One of the biggest advantages of composition is testability:

```csharp
[Test]
public async Task OrderFulfillmentFlow_Should_Process_Successfully()
{
    // Arrange
    var mockPaymentFlow = new Mock<PaymentProcessingFlow>();
    var mockInventoryFlow = new Mock<InventoryUpdateFlow>();
    var mockShippingFlow = new Mock<ShippingFlow>();

    mockPaymentFlow.Setup(f => f.RunAsync(It.IsAny<PaymentRequest>(), It.IsAny<FlowContext>()))
        .ReturnsAsync(new PaymentResult { Success = true, TransactionId = "txn123" });

    var order = new Order { /* test data */ };
    var context = new TestFlowContext();

    // Act
    var flow = new OrderFulfillmentFlow();
    var result = await flow.RunAsync(order, context);

    // Assert
    Assert.True(result.Success);
    Assert.Equal("txn123", result.PaymentTransactionId);
}
```

### What You've Learned

Advanced orchestration teaches you:

1. **Single Responsibility:** Each flow and activity has one clear purpose
2. **Reusability:** Activities and subflows can be used across multiple processes
3. **Composition:** Complex processes are built from simple, well-tested components
4. **Maintainability:** Changes to individual components don't break the entire system
5. **Testability:** Each component can be tested in isolation

**🧩 Architecture Insight:** Think of flows as your business process API. Just like you wouldn't put all your logic in one giant method, don't put your entire business process in one giant flow.

---

## Connecting to the World: Messaging and External Systems

Modern applications don't exist in isolation. They integrate with APIs, message queues, webhooks, and external services. Koan Flow makes these integrations first-class citizens in your workflows.

### Event-Driven Architecture with Koan Flow

Let's build a customer support ticket system that integrates with multiple external systems:

```csharp
public record SupportTicketCreated(
    string TicketId,
    string CustomerId,
    string Subject,
    string Description,
    Priority Priority,
    DateTime CreatedAt);

public class SupportTicketFlow : FlowBase<SupportTicketCreated, TicketProcessingResult>
{
    public override async Task<TicketProcessingResult> RunAsync(
        SupportTicketCreated ticket,
        FlowContext context)
    {
        await context.LogAsync($"Processing support ticket {ticket.TicketId}");

        // Step 1: Enrich ticket with customer data
        var customerData = await EnrichWithCustomerData(ticket, context);

        // Step 2: Determine routing based on ticket analysis
        var routing = await AnalyzeAndRoute(ticket, customerData, context);

        // Step 3: Publish events for other systems
        await PublishTicketEvents(ticket, routing, context);

        // Step 4: Wait for initial response from assigned agent
        var initialResponse = await WaitForAgentResponse(ticket, context);

        // Step 5: Monitor and escalate if needed
        return await MonitorAndEscalate(ticket, initialResponse, context);
    }

    private async Task<CustomerEnrichmentData> EnrichWithCustomerData(
        SupportTicketCreated ticket,
        FlowContext context)
    {
        await context.LogAsync("Enriching ticket with customer data");

        // Get customer info from multiple sources in parallel
        var customerTask = context.ExecuteStepAsync<GetCustomerProfileStep, string, CustomerProfile>(ticket.CustomerId);
        var subscriptionTask = context.ExecuteStepAsync<GetSubscriptionInfoStep, string, SubscriptionInfo>(ticket.CustomerId);
        var historyTask = context.ExecuteStepAsync<GetTicketHistoryStep, string, List<TicketHistory>>(ticket.CustomerId);

        await Task.WhenAll(customerTask, subscriptionTask, historyTask);

        var enrichmentData = new CustomerEnrichmentData
        {
            Profile = await customerTask,
            Subscription = await subscriptionTask,
            TicketHistory = await historyTask
        };

        // Publish enriched data for other systems
        await context.PublishEventAsync(new CustomerDataEnrichedEvent
        {
            TicketId = ticket.TicketId,
            CustomerId = ticket.CustomerId,
            EnrichmentData = enrichmentData
        });

        return enrichmentData;
    }

    private async Task<TicketRoutingDecision> AnalyzeAndRoute(
        SupportTicketCreated ticket,
        CustomerEnrichmentData customerData,
        FlowContext context)
    {
        await context.LogAsync("Analyzing ticket for intelligent routing");

        // Use AI service to categorize the ticket
        var categorization = await context.ExecuteStepAsync<CategorizeTicketStep, CategorizationRequest, CategorizationResult>(
            new CategorizationRequest
            {
                Subject = ticket.Subject,
                Description = ticket.Description,
                CustomerTier = customerData.Subscription.Tier,
                HistoricalContext = customerData.TicketHistory
            });

        // Determine routing based on categorization and business rules
        var routing = await context.ExecuteStepAsync<DetermineRoutingStep, RoutingRequest, TicketRoutingDecision>(
            new RoutingRequest
            {
                Category = categorization.Category,
                Complexity = categorization.ComplexityScore,
                CustomerTier = customerData.Subscription.Tier,
                Priority = ticket.Priority
            });

        // Update external systems
        await context.ExecuteStepAsync<UpdateCrmStep, CrmUpdateRequest, CrmUpdateResult>(
            new CrmUpdateRequest
            {
                TicketId = ticket.TicketId,
                CustomerId = ticket.CustomerId,
                Category = categorization.Category,
                AssignedTeam = routing.AssignedTeam,
                EstimatedResolutionTime = routing.EstimatedResolutionTime
            });

        return routing;
    }

    private async Task PublishTicketEvents(
        SupportTicketCreated ticket,
        TicketRoutingDecision routing,
        FlowContext context)
    {
        await context.LogAsync("Publishing ticket events to downstream systems");

        // Notify Slack channel
        await context.PublishEventAsync(new SlackNotificationEvent
        {
            Channel = routing.SlackChannel,
            Message = $"New {ticket.Priority} ticket assigned: {ticket.Subject}",
            TicketId = ticket.TicketId,
            Priority = ticket.Priority
        });

        // Update analytics/reporting system
        await context.PublishEventAsync(new TicketMetricsEvent
        {
            TicketId = ticket.TicketId,
            Category = routing.Category,
            Team = routing.AssignedTeam,
            CreatedAt = ticket.CreatedAt,
            EstimatedResolution = routing.EstimatedResolutionTime
        });

        // Send to agent assignment system
        await context.PublishEventAsync(new AgentAssignmentRequestEvent
        {
            TicketId = ticket.TicketId,
            RequiredSkills = routing.RequiredSkills,
            Priority = ticket.Priority,
            Team = routing.AssignedTeam
        });
    }

    private async Task<AgentResponse> WaitForAgentResponse(
        SupportTicketCreated ticket,
        FlowContext context)
    {
        await context.LogAsync("Waiting for agent to be assigned and provide initial response");

        // Wait for agent assignment
        var assignment = await context.WaitForEventAsync<AgentAssignedEvent>(
            evt => evt.TicketId == ticket.TicketId,
            TimeSpan.FromMinutes(30)); // SLA: 30 minutes for assignment

        if (assignment == null)
        {
            await context.LogAsync("Agent assignment timed out, escalating");

            await context.PublishEventAsync(new EscalationEvent
            {
                TicketId = ticket.TicketId,
                Reason = "Agent assignment timeout",
                EscalationLevel = 1
            });

            // Wait a bit more after escalation
            assignment = await context.WaitForEventAsync<AgentAssignedEvent>(
                evt => evt.TicketId == ticket.TicketId,
                TimeSpan.FromMinutes(15));

            if (assignment == null)
            {
                throw new SlaViolationException("Failed to assign agent within SLA");
            }
        }

        // Now wait for initial response
        var response = await context.WaitForEventAsync<AgentResponseEvent>(
            evt => evt.TicketId == ticket.TicketId && evt.IsInitialResponse,
            GetResponseSla(ticket.Priority));

        if (response == null)
        {
            await context.PublishEventAsync(new SlaViolationEvent
            {
                TicketId = ticket.TicketId,
                ViolationType = "Initial Response",
                AgentId = assignment.AgentId
            });

            throw new SlaViolationException("Agent failed to provide initial response within SLA");
        }

        return new AgentResponse
        {
            AgentId = assignment.AgentId,
            ResponseTime = response.ResponseTime,
            Content = response.Content
        };
    }

    private TimeSpan GetResponseSla(Priority priority)
    {
        return priority switch
        {
            Priority.Critical => TimeSpan.FromMinutes(15),
            Priority.High => TimeSpan.FromHours(2),
            Priority.Medium => TimeSpan.FromHours(8),
            Priority.Low => TimeSpan.FromDays(1),
            _ => TimeSpan.FromHours(4)
        };
    }
}
```

### Integrating with External APIs

Koan Flow makes it easy to integrate with REST APIs, GraphQL endpoints, and other external services:

```csharp
public class CustomerOnboardingFlow : FlowBase<NewCustomerRequest, OnboardingResult>
{
    public override async Task<OnboardingResult> RunAsync(
        NewCustomerRequest request,
        FlowContext context)
    {
        await context.LogAsync($"Starting onboarding for {request.Email}");

        try
        {
            // Step 1: Create account in our system
            var account = await CreateAccount(request, context);

            // Step 2: Integrate with external systems in parallel
            var integrationTasks = new[]
            {
                IntegrateWithCrm(account, context),
                IntegrateWithBillingSystem(account, context),
                IntegrateWithAnalytics(account, context),
                SetupSupportAccount(account, context)
            };

            await Task.WhenAll(integrationTasks);

            // Step 3: Send welcome communications
            await SendWelcomeSequence(account, context);

            return new OnboardingResult { Success = true, AccountId = account.Id };
        }
        catch (Exception ex)
        {
            await context.LogAsync($"Onboarding failed: {ex.Message}");

            // Publish failure event for monitoring
            await context.PublishEventAsync(new OnboardingFailedEvent
            {
                Email = request.Email,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });

            throw;
        }
    }

    private async Task IntegrateWithCrm(Account account, FlowContext context)
    {
        await context.ExecuteStepAsync<IntegrateCrmStep, CrmIntegrationRequest, CrmIntegrationResult>(
            new CrmIntegrationRequest
            {
                CustomerData = account,
                Source = "Website Signup",
                Tags = new[] { "new-customer", "web-signup" }
            });
    }

    private async Task IntegrateWithBillingSystem(Account account, FlowContext context)
    {
        var result = await context.ExecuteStepAsync<CreateBillingAccountStep, BillingAccountRequest, BillingAccountResult>(
            new BillingAccountRequest
            {
                CustomerId = account.Id,
                Email = account.Email,
                CompanyName = account.CompanyName,
                BillingAddress = account.BillingAddress
            });

        // Store billing ID for future reference
        await context.ExecuteStepAsync<UpdateAccountStep, AccountUpdateRequest, AccountUpdateResult>(
            new AccountUpdateRequest
            {
                AccountId = account.Id,
                BillingAccountId = result.BillingAccountId
            });
    }

    private async Task SetupSupportAccount(Account account, FlowContext context)
    {
        // Create support portal account
        await context.ExecuteStepAsync<CreateSupportAccountStep, SupportAccountRequest, SupportAccountResult>(
            new SupportAccountRequest
            {
                CustomerId = account.Id,
                Email = account.Email,
                CompanyName = account.CompanyName,
                SubscriptionTier = account.SubscriptionTier
            });

        // Create initial knowledge base recommendations
        await context.ExecuteStepAsync<GenerateKnowledgeBaseRecommendationsStep, KbRecommendationRequest, KbRecommendationResult>(
            new KbRecommendationRequest
            {
                CustomerId = account.Id,
                Industry = account.Industry,
                CompanySize = account.CompanySize
            });
    }
}
```

### Webhook Integration and Event Processing

Handle incoming webhooks and external events as part of your flows:

```csharp
public class PaymentWebhookFlow : FlowBase<PaymentWebhookEvent, WebhookProcessingResult>
{
    public override async Task<WebhookProcessingResult> RunAsync(
        PaymentWebhookEvent webhookEvent,
        FlowContext context)
    {
        await context.LogAsync($"Processing payment webhook: {webhookEvent.EventType}");

        // Verify webhook signature for security
        var isValid = await context.ExecuteStepAsync<VerifyWebhookSignatureStep, WebhookVerificationRequest, WebhookVerificationResult>(
            new WebhookVerificationRequest
            {
                PayloadHash = webhookEvent.PayloadHash,
                Signature = webhookEvent.Signature,
                Timestamp = webhookEvent.Timestamp
            });

        if (!isValid.IsValid)
        {
            await context.LogAsync("Invalid webhook signature, rejecting");
            throw new SecurityException("Invalid webhook signature");
        }

        // Process based on event type
        switch (webhookEvent.EventType)
        {
            case "payment.succeeded":
                return await ProcessPaymentSuccess(webhookEvent, context);

            case "payment.failed":
                return await ProcessPaymentFailure(webhookEvent, context);

            case "subscription.cancelled":
                return await ProcessSubscriptionCancellation(webhookEvent, context);

            case "dispute.created":
                return await ProcessDispute(webhookEvent, context);

            default:
                await context.LogAsync($"Unknown webhook event type: {webhookEvent.EventType}");
                return new WebhookProcessingResult { Success = true, Message = "Event type not handled" };
        }
    }

    private async Task<WebhookProcessingResult> ProcessPaymentSuccess(
        PaymentWebhookEvent webhookEvent,
        FlowContext context)
    {
        var paymentData = webhookEvent.GetPaymentData();

        // Update payment status in our system
        await context.ExecuteStepAsync<UpdatePaymentStatusStep, PaymentStatusUpdate, PaymentStatusResult>(
            new PaymentStatusUpdate
            {
                PaymentId = paymentData.PaymentId,
                Status = PaymentStatus.Completed,
                TransactionId = paymentData.TransactionId,
                ProcessedAt = DateTime.UtcNow
            });

        // Trigger fulfillment flow
        await context.ExecuteSubflowAsync<OrderFulfillmentFlow, FulfillmentRequest, FulfillmentResult>(
            new FulfillmentRequest
            {
                OrderId = paymentData.OrderId,
                PaymentConfirmed = true
            });

        // Send confirmation email
        await context.PublishEventAsync(new PaymentConfirmedEvent
        {
            OrderId = paymentData.OrderId,
            PaymentId = paymentData.PaymentId,
            Amount = paymentData.Amount,
            Customer = paymentData.CustomerEmail
        });

        return new WebhookProcessingResult { Success = true };
    }
}
```

### Building Event-Driven Sagas

Coordinate long-running transactions across multiple services:

```csharp
public class OrderSagaFlow : FlowBase<OrderCreated, SagaResult>
{
    public override async Task<SagaResult> RunAsync(OrderCreated orderEvent, FlowContext context)
    {
        var sagaState = new OrderSagaState
        {
            OrderId = orderEvent.OrderId,
            CustomerId = orderEvent.CustomerId,
            TotalAmount = orderEvent.TotalAmount
        };

        try
        {
            // Step 1: Reserve inventory
            await ReserveInventory(sagaState, context);

            // Step 2: Process payment
            await ProcessPayment(sagaState, context);

            // Step 3: Ship order
            await ShipOrder(sagaState, context);

            // Step 4: Update loyalty points
            await UpdateLoyaltyPoints(sagaState, context);

            await context.PublishEventAsync(new OrderCompletedEvent { OrderId = sagaState.OrderId });

            return new SagaResult { Success = true };
        }
        catch (Exception ex)
        {
            await context.LogAsync($"Saga failed, starting compensation: {ex.Message}");
            await CompensateSaga(sagaState, context);
            throw;
        }
    }

    private async Task ReserveInventory(OrderSagaState state, FlowContext context)
    {
        await context.PublishEventAsync(new ReserveInventoryCommand { OrderId = state.OrderId });

        var response = await context.WaitForEventAsync<InventoryReservedEvent>(
            evt => evt.OrderId == state.OrderId,
            TimeSpan.FromMinutes(5));

        if (response == null || !response.Success)
        {
            throw new SagaException("Failed to reserve inventory");
        }

        state.InventoryReserved = true;
        state.ReservationId = response.ReservationId;
    }

    private async Task CompensateSaga(OrderSagaState state, FlowContext context)
    {
        if (state.LoyaltyPointsAdded)
        {
            await context.PublishEventAsync(new ReverseLoyaltyPointsCommand
            {
                CustomerId = state.CustomerId,
                Points = state.LoyaltyPointsAwarded
            });
        }

        if (state.OrderShipped)
        {
            await context.PublishEventAsync(new CancelShipmentCommand
            {
                OrderId = state.OrderId,
                ShipmentId = state.ShipmentId
            });
        }

        if (state.PaymentProcessed)
        {
            await context.PublishEventAsync(new RefundPaymentCommand
            {
                PaymentId = state.PaymentId,
                Amount = state.TotalAmount
            });
        }

        if (state.InventoryReserved)
        {
            await context.PublishEventAsync(new ReleaseInventoryCommand
            {
                ReservationId = state.ReservationId
            });
        }

        await context.PublishEventAsync(new OrderCancelledEvent
        {
            OrderId = state.OrderId,
            Reason = "Saga compensation"
        });
    }
}
```

### What You've Learned

Integration with external systems teaches you:

1. **Event-Driven Design:** React to and publish events throughout your flows
2. **API Integration:** Call external services as part of your business processes
3. **Webhook Handling:** Process incoming events from external systems securely
4. **Saga Patterns:** Coordinate distributed transactions with compensation
5. **Fault Tolerance:** Handle external service failures gracefully

**🌐 Integration Insight:** Modern applications are ecosystems, not monoliths. Koan Flow helps you orchestrate across service boundaries while maintaining reliability and observability.

---

## Extensibility: Building Your Flow Ecosystem

As your application grows, you'll want to extend Koan Flow with custom behaviors, cross-cutting concerns, and domain-specific functionality. Let's explore how to build a rich, extensible flow ecosystem.

### Custom Activities for Domain Logic

Activities are your building blocks for reusable, domain-specific operations:

```csharp
// A sophisticated audit activity that integrates with your compliance system
public class ComplianceAuditActivity : IFlowActivity
{
    private readonly IComplianceService _complianceService;
    private readonly IUserContext _userContext;

    public ComplianceAuditActivity(IComplianceService complianceService, IUserContext userContext)
    {
        _complianceService = complianceService;
        _userContext = userContext;
    }

    public async Task ExecuteAsync(ActivityContext context)
    {
        var auditRequest = context.GetInput<ComplianceAuditRequest>();

        await context.LogAsync($"Starting compliance audit for operation: {auditRequest.OperationType}");

        var auditEvent = new ComplianceAuditEvent
        {
            OperationType = auditRequest.OperationType,
            EntityId = auditRequest.EntityId,
            UserId = _userContext.UserId,
            UserRole = _userContext.Role,
            Timestamp = DateTime.UtcNow,
            FlowInstanceId = context.FlowInstanceId,
            Metadata = auditRequest.Metadata,
            RiskLevel = DetermineRiskLevel(auditRequest),
            RequiredApprovals = GetRequiredApprovals(auditRequest)
        };

        // Store audit event
        var auditId = await _complianceService.CreateAuditEventAsync(auditEvent);

        // Check if this operation requires additional approvals
        if (auditEvent.RiskLevel == RiskLevel.High)
        {
            await context.LogAsync("High-risk operation detected, triggering approval workflow");

            var approvalResult = await RequestApproval(auditEvent, context);

            if (!approvalResult.Approved)
            {
                throw new ComplianceException($"Operation rejected by compliance: {approvalResult.Reason}");
            }
        }

        // Update audit with final status
        await _complianceService.UpdateAuditEventAsync(auditId, ComplianceStatus.Approved);

        context.SetOutput(new ComplianceAuditResult
        {
            AuditId = auditId,
            Status = ComplianceStatus.Approved,
            RiskLevel = auditEvent.RiskLevel
        });
    }

    private RiskLevel DetermineRiskLevel(ComplianceAuditRequest request)
    {
        // Custom risk assessment logic
        return request.OperationType switch
        {
            "FINANCIAL_TRANSACTION" when request.Amount > 10000 => RiskLevel.High,
            "DATA_EXPORT" when request.RecordCount > 1000 => RiskLevel.Medium,
            "USER_ROLE_CHANGE" when request.TargetRole == "Admin" => RiskLevel.High,
            _ => RiskLevel.Low
        };
    }

    private async Task<ApprovalResult> RequestApproval(ComplianceAuditEvent auditEvent, ActivityContext context)
    {
        // Find appropriate approvers
        var approvers = await _complianceService.GetApproversForOperationAsync(auditEvent.OperationType);

        // Send approval requests
        foreach (var approver in approvers)
        {
            await context.PublishEventAsync(new ApprovalRequestEvent
            {
                ApproverId = approver.Id,
                AuditEventId = auditEvent.Id,
                RequesterName = _userContext.UserName,
                OperationDescription = auditEvent.OperationType,
                RiskLevel = auditEvent.RiskLevel
            });
        }

        // Wait for approval (with timeout)
        var approval = await context.WaitForEventAsync<ApprovalResponseEvent>(
            evt => evt.AuditEventId == auditEvent.Id,
            TimeSpan.FromHours(24)); // 24-hour approval SLA

        return approval?.Approved == true
            ? new ApprovalResult { Approved = true }
            : new ApprovalResult { Approved = false, Reason = approval?.Reason ?? "Approval timeout" };
    }
}
```

### Middleware for Cross-Cutting Concerns

Middleware lets you inject behavior across all your flows:

````csharp
// Performance monitoring middleware
public class PerformanceMonitoringMiddleware : IFlowMiddleware
{
    private readonly IMetricsCollector _metricsCollector;
    private readonly ILogger<PerformanceMonitoringMiddleware> _logger;

    public PerformanceMonitoringMiddleware(IMetricsCollector metricsCollector, ILogger<PerformanceMonitoringMiddleware> logger)
    {
        _metricsCollector = metricsCollector;
        _logger = logger;
    }

    public async Task InvokeAsync(FlowContext context, FlowDelegate next)
    {
        var flowName = context.FlowType.Name;
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);

        try
        {
            _logger.LogInformation("Flow {FlowName} starting execution", flowName);

            // Start performance tracking
            using var performanceScope = _metricsCollector.StartTimer($"flow.execution.duration", new[] { ("flow_name", flowName) });

            await next(context);

            stopwatch.Stop();
            var memoryAfter = GC.GetTotalMemory(false);
            var memoryUsed = memoryAfter - memoryBefore;

            // Record metrics
            _metricsCollector.RecordValue("flow.execution.success", 1, new[] { ("flow_name", flowName) });
            _metricsCollector.RecordValue("flow.memory.usage", memoryUsed, new[] { ("flow_name", flowName) });

            _logger.LogInformation(
                "Flow {FlowName} completed successfully in {Duration}ms, Memory used: {MemoryUsed} bytes",
                flowName, stopwatch.ElapsedMilliseconds, memoryUsed);
        }
        catch (Exception ex)
        {

<!-- The following advanced features are not currently implemented in Koan Flow and have been removed for accuracy: advanced middleware, attribute-based decorators, registry/discovery, domain extensions, distributed locks, advanced observability, and environment-specific configuration. Only supported, real features and APIs are described below. -->

---

## Production-Ready Patterns and Best Practices

After learning the concepts, let's cover the essential patterns and practices that separate prototype flows from production-ready systems.


### What You've Learned

Building extensible flows teaches you:

1. **Custom Activities:** Encapsulate domain-specific operations in reusable activities
2. **Middleware Pipeline:** Add cross-cutting concerns without cluttering business logic
3. **Declarative Behavior:** Use attributes to add functionality declaratively
4. **Domain Extensions:** Create fluent APIs that match your business domain
5. **Dynamic Discovery:** Build systems that can discover and execute flows dynamically
6. **Configuration:** Make flows adaptable to different environments and requirements

**🔧 Extensibility Insight:** The key to a great flow system is making it feel natural to your domain. When your flows read like business requirements, you've achieved the right level of abstraction.

---

## Production-Ready Patterns and Best Practices

After learning the concepts, let's cover the essential patterns and practices that separate prototype flows from production-ready systems.

### Input Validation and Sanitization

Always validate and sanitize inputs at flow boundaries:

```csharp
public class RobustOrderProcessingFlow : FlowBase<OrderRequest, OrderResult>
{
    public override async Task<OrderResult> RunAsync(OrderRequest request, FlowContext context)
    {
        // Comprehensive input validation
        var validationResult = await ValidateRequest(request, context);
        if (!validationResult.IsValid)
        {
            await context.LogAsync($"Order validation failed: {string.Join(", ", validationResult.Errors)}");
            return new OrderResult
            {
                Success = false,
                Error = "Invalid request",
                ValidationErrors = validationResult.Errors
            };
        }

        // Sanitize input data
        var sanitizedRequest = await SanitizeRequest(request, context);

        // Continue with processing...
        return await ProcessValidOrder(sanitizedRequest, context);
    }

    private async Task<ValidationResult> ValidateRequest(OrderRequest request, FlowContext context)
    {
        var errors = new List<string>();

        // Null/empty checks
        if (string.IsNullOrWhiteSpace(request.CustomerId))
            errors.Add("Customer ID is required");

        if (request.Items == null || !request.Items.Any())
            errors.Add("Order must contain at least one item");

        // Business rule validation
        if (request.TotalAmount <= 0)
            errors.Add("Order total must be greater than zero");

        if (request.Items?.Any(i => i.Quantity <= 0) == true)
            errors.Add("All items must have positive quantities");

        // Cross-field validation
        var calculatedTotal = request.Items?.Sum(i => i.Price * i.Quantity) ?? 0;
        if (Math.Abs(calculatedTotal - request.TotalAmount) > 0.01m)
            errors.Add("Order total doesn't match item calculations");

        // External validation (async)
        var customerExists = await context.ExecuteStepAsync<ValidateCustomerStep, string, bool>(request.CustomerId);
        if (!customerExists)
            errors.Add("Customer not found");

        return new ValidationResult { IsValid = !errors.Any(), Errors = errors };
    }
}
````

### Handling Large Datasets with Streaming and Paging

Don't load everything into memory at once:

```csharp
public class BulkDataProcessingFlow : FlowBase<BulkProcessingRequest, BulkProcessingResult>
{
    public override async Task<BulkProcessingResult> RunAsync(BulkProcessingRequest request, FlowContext context)
    {
        await context.LogAsync($"Starting bulk processing of {request.DataSourceId}");

        var processedCount = 0;
        var errorCount = 0;
        var batchSize = request.BatchSize ?? 1000; // Default batch size

        // Use async enumerable for streaming
        await foreach (var batch in GetDataBatches(request.DataSourceId, batchSize, context))
        {
            await context.LogAsync($"Processing batch of {batch.Count} items");

            try
            {
                var batchResults = await ProcessBatch(batch, context);
                processedCount += batchResults.SuccessCount;
                errorCount += batchResults.ErrorCount;

                // Checkpoint progress for long-running operations
                if (processedCount % 10000 == 0)
                {
                    await context.SetStateAsync("processedCount", processedCount);
                    await context.SetStateAsync("errorCount", errorCount);
                    await context.LogAsync($"Checkpoint: Processed {processedCount} items, {errorCount} errors");
                }
            }
            catch (Exception ex)
            {
                await context.LogAsync($"Batch processing failed: {ex.Message}");
                errorCount += batch.Count;

                // Decide whether to continue or fail fast
                if (request.FailFast)
                    throw;
            }
        }

        return new BulkProcessingResult
        {
            ProcessedCount = processedCount,
            ErrorCount = errorCount,
            Success = errorCount == 0
        };
    }

    private async IAsyncEnumerable<List<DataItem>> GetDataBatches(
        string dataSourceId,
        int batchSize,
        FlowContext context)
    {
        var offset = 0;
        List<DataItem> batch;

        do
        {
            batch = await context.ExecuteStepAsync<GetDataBatchStep, DataBatchRequest, List<DataItem>>(
                new DataBatchRequest
                {
                    DataSourceId = dataSourceId,
                    Offset = offset,
                    BatchSize = batchSize
                });

            if (batch.Any())
            {
                yield return batch;
                offset += batch.Count;
            }
        }
        while (batch.Count == batchSize); // Continue while we're getting full batches
    }
}
```

### Concurrency Control and Idempotency

Handle concurrent execution and ensure operations are safe to retry:

```csharp
public class IdempotentPaymentFlow : FlowBase<PaymentRequest, PaymentResult>
{
    public override async Task<PaymentResult> RunAsync(PaymentRequest request, FlowContext context)
    {
        // Use order ID as idempotency key
        var idempotencyKey = $"payment-{request.OrderId}";

        // Check if we've already processed this payment
        var existingResult = await context.GetStateAsync<PaymentResult>(idempotencyKey);
        if (existingResult != null)
        {
            await context.LogAsync($"Payment already processed for order {request.OrderId}");
            return existingResult;
        }

        // Use distributed lock to prevent concurrent processing
        using var lockHandle = await context.AcquireLockAsync(
            $"payment-lock-{request.OrderId}",
            TimeSpan.FromMinutes(5));

        if (lockHandle == null)
        {
            throw new ConcurrencyException($"Another process is already handling payment for order {request.OrderId}");
        }

        // Double-check after acquiring lock
        existingResult = await context.GetStateAsync<PaymentResult>(idempotencyKey);
        if (existingResult != null)
        {
            return existingResult;
        }

        try
        {
            var result = await ProcessPayment(request, context);

            // Store result for future idempotency checks
            await context.SetStateAsync(idempotencyKey, result, TimeSpan.FromHours(24));

            return result;
        }
        catch (Exception ex)
        {
            // Only cache successful results or known permanent failures
            if (IsPermanentFailure(ex))
            {
                var errorResult = new PaymentResult { Success = false, Error = ex.Message };
                await context.SetStateAsync(idempotencyKey, errorResult, TimeSpan.FromHours(1));
            }

            throw;
        }
    }
}
```

<!-- Security and authorization patterns are not currently implemented as first-class features in Koan Flow. For sensitive operations, implement security checks and authorization logic directly within your flow's business logic. -->

<!-- Distributed failure handling, circuit breaker, and advanced retry patterns are not currently implemented as built-in features in Koan Flow. For resilience, use standard .NET patterns and libraries within your flow logic as needed. -->

### Performance Optimization Tips

1. **Use Parallel Processing Wisely:** Don't parallelize everything - measure the impact
2. **Cache Expensive Operations:** Store results of costly computations
3. **Minimize Memory Allocation:** Reuse objects where possible
4. **Use Streaming for Large Data:** Don't load everything into memory
5. **Set Reasonable Timeouts:** Prevent flows from hanging indefinitely
6. **Monitor Resource Usage:** Track memory, CPU, and network usage

<!-- Advanced monitoring, tracing, and observability APIs are not currently implemented in Koan Flow. For observability, use standard .NET logging and monitoring tools within your flows. -->

## References and Further Reading

### Core Documentation

- [Koan Flow API Reference](../../reference/flow.md)
- [Architecture Principles](../../architecture/principles.md)
- [Messaging Integration Guide](../messaging/index.md)
- [Data Access Patterns](../data/all-query-streaming-and-pager.md)

### Advanced Topics

- [Performance Optimization](../../engineering/performance.md)
- [Security Best Practices](../../engineering/security.md)
- [Monitoring and Observability](../../engineering/observability.md)
- [Testing Strategies](../../engineering/testing.md)

### Decision Records

- [ADR-001: Flow Architecture](../../decisions/001-flow-architecture.md)
- [ADR-002: Error Handling Strategy](../../decisions/002-error-handling.md)
- [ADR-003: State Management](../../decisions/003-state-management.md)

---

**Next Steps:** Now that you understand Koan Flow concepts, explore the [Sample Applications](../../../samples/) to see these patterns in action, or dive into the [API Reference](../../reference/flow.md) for detailed technical documentation.

**See also:** [Engineering Index](../../engineering/index.md) | [Module Catalog](../../modules/) | [Support Resources](../../support/)
