using System.Security.Claims;
using ItsTool.API.Security;
using ItsTool.Application.Constants;
using Microsoft.AspNetCore.Authorization;

namespace ItsTool.UnitTests.Security;

/// <summary>
/// SuperAdmin passes every permission check except the security permissions, which need a real grant.
/// </summary>
public class ExplicitPermissionTests
{
    private static async Task<bool> PassesAsync(string permission, params Claim[] claims)
    {
        var context = new AuthorizationHandlerContext(new[] { new PermissionRequirement(permission) }, new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), null);
        await new PermissionAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task SuperAdmin_needs_a_grant_for_the_security_permissions()
    {
        var superAdmin = new Claim(ClaimTypes.Role, "SuperAdmin");

        Assert.True(await PassesAsync(PermissionConstants.AuditView, superAdmin));
        Assert.False(await PassesAsync(PermissionConstants.SecurityIncidentView, superAdmin));
        Assert.False(await PassesAsync(PermissionConstants.SecurityIncidentManage, superAdmin));
        Assert.True(await PassesAsync(PermissionConstants.SecurityIncidentView, superAdmin, new Claim("permission", PermissionConstants.SecurityIncidentView)));
    }

    [Fact]
    public void The_security_permissions_are_registered_and_explicit_only()
    {
        Assert.Contains(PermissionConstants.SecurityIncidentView, PermissionConstants.AllPermissions);
        Assert.Contains(PermissionConstants.SecurityIncidentManage, PermissionConstants.AllPermissions);
        Assert.Equal(new[] { PermissionConstants.SecurityIncidentView, PermissionConstants.SecurityIncidentManage }, PermissionConstants.ExplicitOnly);
    }
}
