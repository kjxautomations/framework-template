using Framework.Devices;
using Framework.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FrameworkTest;

[TestFixture]
public class TestNotificationService
{
    [Test]
    public void TestEventsOccurInRightContext()
    {
        ManualResetEvent gotIt = new ManualResetEvent(false);
        var currentContext = new BackgroundThreadSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(currentContext);
        var logger = new Mock<ILogger<INotificationService>>().Object;
        var svc = new InMemoryNotificationService(currentContext) { Logger = logger };
        SynchronizationContext threadContext = null;
        SynchronizationContext notificationContext = null;
        svc.Notifications.CollectionChanged += (sender, args) =>
        {
            notificationContext = SynchronizationContext.Current;
            gotIt.Set();
        };
        var t = new Thread(() =>
        {
            threadContext = SynchronizationContext.Current;
            svc.AddNotification(NotificationType.Error, "Bad juju");
        });
        t.Start();
        t.Join();
        gotIt.WaitOne();
        Assert.That(currentContext, Is.SameAs(notificationContext));
        Assert.That(notificationContext, Is.Not.SameAs(threadContext));

        gotIt.Reset();
        var notification = svc.Notifications.First();
        notification.PropertyChanged += (sender, args) =>
        {
            notificationContext = SynchronizationContext.Current;
            gotIt.Set();
        };
        // now mark it as read
        notification.HasBeenRead = true;
        gotIt.WaitOne();
        Assert.That(currentContext, Is.SameAs(notificationContext));
        
        currentContext.Shutdown();
    }
}