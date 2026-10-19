namespace OrderService.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "/";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string ExchangeName { get; set; } = "ecommerce.events";
    public string DeadLetterExchangeName { get; set; } = "ecommerce.events.dlx";
    public bool Enabled { get; set; } = true;
}
