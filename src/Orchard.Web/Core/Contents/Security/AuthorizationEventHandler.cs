using JetBrains.Annotations;
using Orchard.ContentManagement;
using Orchard.ContentManagement.Aspects;
using Orchard.Core.Contents.Settings;
using Orchard.Security;
using Orchard.Security.Permissions;

namespace Orchard.Core.Contents.Security
{
    [UsedImplicitly]
    public class AuthorizationEventHandler : IAuthorizationServiceEventHandler
    {
        public void Checking(CheckAccessContext context) { }
        public void Complete(CheckAccessContext context) { }

        public void Adjust(CheckAccessContext context) {
            if ( !context.Granted &&
                context.Content.Is<ICommonPart>() ) {

                if (OwnerVariationExists(context.Permission) &&
                    HasOwnership(context.User, context.Content)) {

                    context.Adjusted = true;
                    context.Permission = GetOwnerVariation(context.Permission);
                }

                var typeDefinition = context.Content.ContentItem.TypeDefinition;

                // replace permission if a content type specific version exists
                if ( typeDefinition.Settings.GetModel<ContentTypeSettings>().Creatable ) {
                    var permission = GetContentTypeVariation(context.Permission);

                    if ( permission != null) {
                        context.Adjusted = true;
                        context.Permission = DynamicPermissions.CreateDynamicPermission(permission, typeDefinition);
                    }
                }
            }
        }

        private static bool HasOwnership(IUser user, IContent content) {
            if (user == null || content == null)
                return false;

            var common = content.As<ICommonPart>();
            if (common == null || common.Owner == null)
                return false;

            return user.Id == common.Owner.Id;
        }

        private static bool OwnerVariationExists(Permission permission) {
            return GetOwnerVariation(permission) != null;
        }

        private static Permission GetOwnerVariation(Permission permission) {
            if (permission.Name == Permissions.PublishOthersContent.Name)
                return Permissions.PublishOwnContent;
            if (permission.Name == Permissions.EditOthersContent.Name)
                return Permissions.EditOwnContent;
            if (permission.Name == Permissions.DeleteOthersContent.Name)
                return Permissions.DeleteOwnContent;
            return null;
        }

        private static Permission GetContentTypeVariation(Permission permission) {
            return DynamicPermissions.ConvertToDynamicPermission(permission);
        }
    }
}