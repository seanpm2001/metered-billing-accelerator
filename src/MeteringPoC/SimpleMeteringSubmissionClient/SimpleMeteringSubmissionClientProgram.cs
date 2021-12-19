﻿using System.Security.Cryptography;
using System.Text;
using Azure.Messaging.EventHubs.Producer;
using Metering.Types;
using Metering.ClientSDK;
using MeterValueModule = Metering.ClientSDK.MeterValueModule;

Console.Title = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name;

static string guidFromStr(string str) => new Guid(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(str)).Take(16).ToArray()).ToString();

static async Task<T> readJson<T>(string name) => Json.fromStr<T>(await File.ReadAllTextAsync(name));

using CancellationTokenSource cts = new();

var eventHubProducerClient = 
        MeteringConnectionsModule
            .getClientConfigFromEnvironment()
            .createEventHubProducerClientForClientSDK();

// await Interactive(eventHubProducerClient, cts.Token);
await Batch(eventHubProducerClient, "1", cts.Token);

cts.Cancel();

static async Task Batch(EventHubProducerClient eventHubProducerClient, string subName, CancellationToken ct)
{
    int i = 0;
    var saasId = guidFromStr(subName);
    while (true)
    {
        var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                   .Select(x => MeterValueModule.create(x, 0.1))
                   .ToArray();

        if (i++ % 10 == 0) { Console.Write("."); }

        await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(saasId, meters), ct);
        await Task.Delay(TimeSpan.FromSeconds(0.3), ct);
    }
}

#pragma warning disable CS8321 // Local function is declared but never used
static async Task Interactive(EventHubProducerClient eventHubProducerClient, CancellationToken ct)
#pragma warning restore CS8321 // Local function is declared but never used
{
    static ulong GetQuantity(string command) => command.Split(' ').Length > 2 ? ulong.Parse(command.Split(' ')[2]) : 1;

    try
    {
        var (subName, saasId) = ("", "");
        while (true)
        {
            await Console.Out.WriteLineAsync("c sub or s sub to submit to a particular subscription>  ");
            var command = await Console.In.ReadLineAsync();


            if (command.ToLower().StartsWith("c"))
            {
                subName = command.Split(' ')[1];
                saasId = guidFromStr(subName);

                SubscriptionCreationInformation sci = new(
                    internalMetersMapping: await readJson<InternalMetersMapping>("mapping.json"),
                    subscription: new(
                        plan: await readJson<Plan>("plan.json"),
                        internalResourceId: InternalResourceIdModule.fromStr(saasId),
                        renewalInterval: RenewalInterval.Monthly,
                        subscriptionStart: MeteringDateTimeModule.now()));

                await Console.Out.WriteLineAsync($"Creating subscription {subName} ({saasId})");
                await eventHubProducerClient.SubmitSubscriptionCreationAsync(sci, ct);
            }
            else if (command.ToLower().StartsWith("s"))
            {
                subName = command.Split(' ')[1];
                saasId = guidFromStr(subName);
                var count = GetQuantity(command);

                var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                    .Select(x => MeterValueModule.create(x, count))
                    .ToArray();

                await Console.Out.WriteLineAsync($"Emitting to name={subName} (partitionKey={saasId})");
                await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(saasId, meters), ct);
            }
            else
            {
                await Console.Out.WriteLineAsync($"Emitting to {subName} ({saasId})");
                var meters = new[] { "nde", "cpu", "dta", "msg", "obj" }
                    .Select(x => MeterValueModule.create(x, 1.0))
                    .ToArray();

                await Console.Out.WriteLineAsync($"Emitting to name={subName} (partitionKey={saasId})");
                await eventHubProducerClient.SubmitSaaSMeterAsync(SaaSConsumptionModule.create(saasId, meters), ct);
            }
        }
    }
    catch (Exception e)
    {
        await Console.Error.WriteLineAsync(e.Message);
    }
    finally
    {
        await eventHubProducerClient.CloseAsync(ct);
    }
}