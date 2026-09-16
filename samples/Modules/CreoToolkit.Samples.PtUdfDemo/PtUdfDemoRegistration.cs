using CreoToolkit.App;
using CreoToolkit.Sdk.Events;

namespace CreoToolkit.Samples.PtUdfDemo;

/// <summary>GroupUngroupPre sample:订阅 ungroup 拦截事件,
/// handler 总返 Block 演示拦截语义。订阅经 Session.TrackResource 自动 Session.Dispose 释放,
/// sample 不需手 Dispose。</summary>
public static class PtUdfDemoRegistration
{
    private const string LogModule = "PtUdfDemo";

    public static void Register(CreoAppBuilder app)
    {
        ThrowUtil.IfNull(app);

        app.Command("pt.udf.subscribe-block-ungroup",
            "PT_UDF_SUBSCRIBE_BLOCK_UNGROUP",
            "PT_UDF_SUBSCRIBE_BLOCK_UNGROUP_HELP", ctx =>
        {
            if (ctx.Session is null)
            {
                ctx.Messages.Info("PT_UDF_NO_SESSION");
                return;
            }

            _ = ctx.Session.Events.SubscribeGroupUngroupPre(context =>
            {
                CreoAppLog.Info(
                    $"ungroup blocked: group={context.Name ?? "<unnamed>"} model={context.OwnerModelName} isTd={context.IsTabledriven} members={context.MemberFeatureIds.Count}",
                    @event: "udf.ungroup.blocked",
                    module: LogModule,
                    props: new
                    {
                        groupId = context.GroupId,
                        groupName = context.Name,
                        model = context.OwnerModelName,
                        modelType = context.OwnerModelType.ToString(),
                        isTabledriven = context.IsTabledriven,
                        memberCount = context.MemberFeatureIds.Count,
                    });
                return UngroupVote.Block;
            });

            CreoAppLog.Info(
                "GroupUngroupPre subscribed (auto-released on Session.Dispose)",
                @event: "udf.subscribe.ok",
                module: LogModule);

            ctx.Messages.Info("GroupUngroupPre subscribed. Try Edit -> Ungroup in Creo UI to see block.");
        });

    }
}
