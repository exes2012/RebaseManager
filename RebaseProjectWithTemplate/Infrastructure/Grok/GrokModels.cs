namespace RebaseProjectWithTemplate.Infrastructure.Grok;

public class GrokRequest
{
    public string Model { get; set; }
    public List<GrokMessage> Messages { get; set; }
    public bool Stream { get; set; }
    public double Temperature { get; set; }
}

public class GrokMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}

public class GrokResponse
{
    public List<GrokChoice> Choices { get; set; }
}

public class GrokChoice
{
    public GrokMessage Message { get; set; }
}