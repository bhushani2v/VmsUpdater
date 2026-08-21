using System.Text;
using System.Text.Json;
using MQTTnet;
using MQTTnet.Protocol;
using VmsUpdater.Models;

namespace VmsUpdater.Services;

public class StatusReporter : IAsyncDisposable
{
    private const string DefaultTopic = "vms/updater/status";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IMqttClient _mqttClient;
    private bool _connected;

    public StatusReporter()
    {
        _mqttClient = new MqttClientFactory().CreateMqttClient();
    }

    public async Task ConnectAsync(string host = "localhost", int port = 1883)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(host, port)
            .WithClientId($"VmsUpdater-{Environment.ProcessId}")
            .WithCleanSession()
            .Build();

        try
        {
            await _mqttClient.ConnectAsync(options);
            _connected = true;
            Console.Error.WriteLine($"MQTT connected to {host}:{port}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MQTT connection failed ({host}:{port}): {ex.Message}");
            Console.Error.WriteLine("Falling back to console output.");
            _connected = false;
        }
    }

    public void Push(StatusUpdate update)
    {
        var json = JsonSerializer.Serialize(update, JsonOptions);
        PublishAsync(json).GetAwaiter().GetResult();
    }

    public void PushResult(UpdateResult result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        PublishAsync(json).GetAwaiter().GetResult();
    }

    private async Task PublishAsync(string payload)
    {
        if (_connected)
        {
            try
            {
                var message = new MqttApplicationMessageBuilder()
                    .WithTopic(DefaultTopic)
                    .WithPayload(Encoding.UTF8.GetBytes(payload))
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient.PublishAsync(message);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"MQTT publish failed: {ex.Message}");
                // Fallback to console
                Console.WriteLine(payload);
                Console.Out.Flush();
            }
        }
        else
        {
            // Fallback: write to stdout if MQTT is not connected
            Console.WriteLine(payload);
            Console.Out.Flush();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connected)
        {
            try
            {
                await _mqttClient.DisconnectAsync();
            }
            catch
            {
                // Ignore disconnect errors during shutdown
            }
        }

        _mqttClient.Dispose();
    }
}
