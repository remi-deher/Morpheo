[AttributeUsage(AttributeTargets.Class)]
public class MorpheoTypeAttribute : Attribute
{
    public string Name { get; }

    public MorpheoTypeAttribute(string name)
    {
        Name = name;
    }
}
