namespace OrderService.Domain.Orders;

public enum OrderStatus
{
    Pending = 1,
    StockReserved = 2,
    Confirmed = 3,
    Rejected = 4,
    Cancelled = 5
}

public enum CancelledBy
{
    Customer = 1,
    Admin = 2,
    System = 3
}

public enum TransitionResult
{
    Applied = 1,
    NoChange = 2,
    NotAllowed = 3
}
