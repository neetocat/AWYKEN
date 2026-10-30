window.awykenNotifications = window.awykenNotifications || {};

let reminderAudio;

window.awykenNotifications.playReminderSound = function () {
    if (!reminderAudio) {
        reminderAudio = new Audio("_content/AWYKEN.Shared/sound/universfield-new-notification-09-352705.mp3");
        reminderAudio.loop = true;
    }

    return reminderAudio.play();
};

window.awykenNotifications.stopReminderSound = function () {
    if (!reminderAudio) {
        return;
    }

    reminderAudio.pause();
    reminderAudio.currentTime = 0;
};
