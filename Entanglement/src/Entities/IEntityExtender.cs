namespace Entanglement.Entities
{
    /// <summary>Fusion IEntityExtender — attaches behaviour to a <see cref="NetworkEntity"/>.</summary>
    public interface IEntityExtender
    {
        NetworkEntity NetworkEntity { get; }
    }
}
