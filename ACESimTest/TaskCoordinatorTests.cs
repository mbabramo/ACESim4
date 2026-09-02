using ACESimBase.Util.TaskManagement;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ACESimTest
{
    [TestClass]
    public class TaskCoordinatorTests
    {
        [TestMethod]
        public void SequentialWorkers_ClaimDistinctTasks_AndDuplicateCompletionIsRejected()
        {
            TaskCoordinator coordinator = CreateCoordinator(3);
            coordinator.Update(null, null, true, 1, out var firstClaim, out _);
            byte[] afterFirstClaim = coordinator.StatusAsByteArray();

            TaskCoordinator secondWorkerView = CreateCoordinator(3);
            secondWorkerView.StatusFromByteArray(afterFirstClaim);
            secondWorkerView.Update(null, null, true, 1, out var secondClaim, out _);

            firstClaim.Single().ID.Should().NotBe(secondClaim.Single().ID);

            secondWorkerView.Update(firstClaim, null, true, 1, out _, out _);
            Action duplicate = () => secondWorkerView.Update(firstClaim, null, true, 1, out _, out _);
            duplicate.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate terminal result*");
        }

        [TestMethod]
        public void FailedAndPendingTasks_AreDetectedAndRequireExplicitReset()
        {
            TaskCoordinator coordinator = CreateCoordinator(2);
            coordinator.Update(null, null, true, 1, out var firstClaim, out _);
            coordinator.Update(null, firstClaim, true, 1, out var secondClaim, out _);

            coordinator.NumTasksFailed.Should().Be(1);
            coordinator.HasFailures.Should().BeTrue();
            secondClaim.Should().BeNull("a failed first stage must block further work");

            coordinator.ResetFailedTasks().Should().Be(1);
            coordinator.Update(null, null, true, 1, out var retryClaim, out _);
            retryClaim.Single().ID.Should().Be(firstClaim.Single().ID);
            coordinator.NumTasksPending.Should().Be(1);
            coordinator.ResetPendingTasks().Should().Be(1);
            coordinator.NumTasksUnstarted.Should().Be(2);
        }

        [TestMethod]
        public void SerializedStatus_RejectsAChangedTaskPlan()
        {
            byte[] twoTaskStatus = CreateCoordinator(2).StatusAsByteArray();
            Action loadChangedPlan = () => CreateCoordinator(3).StatusFromByteArray(twoTaskStatus);
            loadChangedPlan.Should().Throw<InvalidDataException>().WithMessage("*contains 2 tasks*this launcher defines 3*");
        }

        [TestMethod]
        public void SerializedStatus_RejectsChangedOptionSetWithSameTaskCount()
        {
            byte[] originalStatus = CreateCoordinator(2, "original").StatusAsByteArray();
            Action loadChangedPlan = () => CreateCoordinator(2, "changed").StatusFromByteArray(originalStatus);
            loadChangedPlan.Should().Throw<InvalidDataException>().WithMessage("*plan fingerprint does not match*");
        }

        [TestMethod]
        public void CompletedStatus_RoundTripsDeterministically()
        {
            TaskCoordinator coordinator = CreateCoordinator(2);
            coordinator.Update(null, null, true, 2, out var claims, out _);
            coordinator.Update(claims, null, false, 1, out _, out _);
            byte[] first = coordinator.StatusAsByteArray();

            TaskCoordinator restored = CreateCoordinator(2);
            restored.StatusFromByteArray(first);
            restored.AllComplete.Should().BeTrue();
            restored.StatusAsByteArray().Should().Equal(first);
        }

        private static TaskCoordinator CreateCoordinator(int taskCount, string planPrefix = "option") =>
            new(new List<TaskStage>
            {
                new(Enumerable.Range(0, taskCount)
                    .Select(id => new RepeatedTask("Optimize", id, 1, null, $"{planPrefix}-{id}"))
                    .ToList()),
            });
    }
}
