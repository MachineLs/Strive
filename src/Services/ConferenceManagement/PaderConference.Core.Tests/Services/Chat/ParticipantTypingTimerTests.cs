﻿using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Moq;
using PaderConference.Core.Services;
using PaderConference.Core.Services.Chat;
using PaderConference.Core.Services.Chat.Requests;
using PaderConference.Tests.Utils;
using Xunit;

namespace PaderConference.Core.Tests.Services.Chat
{
    public class ParticipantTypingTimerTests
    {
        private const string ConferenceId = "conferenceId";
        private const string Channel = "testChatChannel";

        private readonly Participant _testParticipant = new(ConferenceId, "123");
        private readonly Mock<IMediator> _mediator = new();
        private ITaskDelay _taskDelay = new TaskDelay();

        private ParticipantTypingTimer Create()
        {
            return new(_mediator.Object, _taskDelay);
        }

        private Action SetupTaskDelayGetTrigger()
        {
            var task = new TaskCompletionSource();

            var taskDelay = new Mock<ITaskDelay>();
            taskDelay.Setup(x => x.Delay(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>())).Returns(task.Task);

            _taskDelay = taskDelay.Object;

            return () => task.SetResult();
        }

        [Fact]
        public void RemoveParticipantTypingAfter_WaitTimeout_SendRequest()
        {
            // arrange
            var trigger = SetupTaskDelayGetTrigger();

            var timer = Create();
            var capturedRequest = _mediator.CaptureRequest<SetParticipantTypingRequest, Unit>();

            // act
            timer.RemoveParticipantTypingAfter(_testParticipant, Channel, TimeSpan.FromMilliseconds(50));

            // assert
            capturedRequest.AssertNotReceived();

            trigger();

            capturedRequest.AssertReceived();
        }

        [Fact]
        public async Task RemoveParticipantTypingAfter_AddMultiple_SendAllRequests()
        {
            // arrange
            var timer = Create();

            var receivedRequests = new ConcurrentBag<SetParticipantTypingRequest>();
            _mediator.Setup(x => x.Send(It.IsAny<SetParticipantTypingRequest>(), It.IsAny<CancellationToken>()))
                .Callback((IRequest<Unit> request, CancellationToken _) =>
                    receivedRequests.Add((SetParticipantTypingRequest) request));

            // act
            var expectedRequests = new ConcurrentBag<SetParticipantTypingRequest>();
            var participantIdCounter = 0;

            async Task TestRemoveParticipant()
            {
                var random = new Random();

                for (var i = 0; i < 200; i++)
                {
                    await Task.Delay(random.Next(0, 2));

                    var participantId = Interlocked.Increment(ref participantIdCounter).ToString();
                    var participant = new Participant(ConferenceId, participantId);
                    var delay = TimeSpan.FromMilliseconds(random.Next(0, 31));

                    timer.RemoveParticipantTypingAfter(participant, Channel, delay);

                    expectedRequests.Add(new SetParticipantTypingRequest(participant, Channel, false));
                }
            }

            var tasks = Enumerable.Range(0, 4).Select(x => Task.Run(TestRemoveParticipant)).ToList();
            await Task.WhenAll(tasks);

            await Task.Delay(100);

            // assert
            AssertHelper.AssertScrambledEquals(expectedRequests, receivedRequests);
        }

        [Fact]
        public void CancelTimer_TimerWasNotSet_DoNothing()
        {
            // arrange
            var timer = Create();

            // act
            timer.CancelTimer(_testParticipant, Channel);
        }

        [Fact]
        public void CancelTimer_TimerWasSet_NoRequest()
        {
            // arrange
            var trigger = SetupTaskDelayGetTrigger();

            var timer = Create();
            var capturedRequest = _mediator.CaptureRequest<SetParticipantTypingRequest, Unit>();

            timer.RemoveParticipantTypingAfter(_testParticipant, Channel, TimeSpan.FromMilliseconds(100));

            // act
            timer.CancelTimer(_testParticipant, Channel);

            // assert
            trigger();
            capturedRequest.AssertNotReceived();
        }

        [Fact]
        public void CancelTimer_TimerWasSet_Reschedule()
        {
            const string channel2 = "testChannel2";

            // arrange
            var trigger = SetupTaskDelayGetTrigger();

            var timer = Create();
            var capturedRequest = _mediator.CaptureRequest<SetParticipantTypingRequest, Unit>();

            timer.RemoveParticipantTypingAfter(_testParticipant, Channel, TimeSpan.FromMilliseconds(100));
            timer.RemoveParticipantTypingAfter(_testParticipant, channel2, TimeSpan.FromMilliseconds(101));

            // act
            timer.CancelTimer(_testParticipant, Channel);

            // assert
            trigger();
            capturedRequest.AssertReceived();

            var request = capturedRequest.GetRequest();
            Assert.Equal(channel2, request.Channel);
        }

        [Fact]
        public void CancelAllTimers_TimerWasNotSet_DoNothing()
        {
            // arrange
            var timer = Create();

            // act
            var timers = timer.CancelAllTimers(_testParticipant);

            // assert
            Assert.Empty(timers);
        }

        [Fact]
        public void CancelAllTimers_TimerWasSet_CancelAndReturnChannel()
        {
            // arrange
            var trigger = SetupTaskDelayGetTrigger();

            var timer = Create();
            var capturedRequest = _mediator.CaptureRequest<SetParticipantTypingRequest, Unit>();

            timer.RemoveParticipantTypingAfter(_testParticipant, Channel, TimeSpan.FromMilliseconds(100));

            // act
            var timers = timer.CancelAllTimers(_testParticipant);

            // assert
            Assert.Equal(Channel, Assert.Single(timers));

            trigger();
            capturedRequest.AssertNotReceived();
        }
    }
}
