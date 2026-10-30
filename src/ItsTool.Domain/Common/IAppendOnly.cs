namespace ItsTool.Domain.Common;

/// <summary>Records that may be added but never changed or deleted; the audit interceptor refuses the save.</summary>
public interface IAppendOnly
{
}
