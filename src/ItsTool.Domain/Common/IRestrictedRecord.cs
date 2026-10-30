namespace ItsTool.Domain.Common;

/// <summary>
/// Records only a few people may read, such as security incidents. They are kept out of the system audit log,
/// which every audit.view holder can read, and keep their own append-only history instead.
/// </summary>
public interface IRestrictedRecord
{
}
