class MeetingTimer {
    constructor() {
        this.isRunning = false;
        this.updateInterval = null;

        // Состояние таймера
        this.state = {
            totalDuration: 0, // в секундах
            originalDuration: 0, // исходная длительность
            participants: 0,
            currentParticipant: 1,
            startTime: null,
            extraTimeAdded: 0,
            remainingTime: 0,
            timePerParticipant: 0,
            elapsedTime: 0 // общее прошедшее время
        };

        this.initializeEventListeners();
    }

    initializeEventListeners() {
        document.getElementById('startBtn').addEventListener('click', () => this.startMeeting());
        document.getElementById('addTimeBtn').addEventListener('click', () => this.addTime());
        document.getElementById('nextParticipantBtn').addEventListener('click', () => this.nextParticipant());
        document.getElementById('stopBtn').addEventListener('click', () => this.stopMeeting());
    }

    startMeeting() {
        const duration = parseInt(document.getElementById('duration').value);
        const participants = parseInt(document.getElementById('participants').value);

        if (!duration || !participants || duration <= 0 || participants <= 0) {
            this.showStatus('Пожалуйста, введите корректные значения', 'warning');
            return;
        }

        // Сбрасываем состояние
        this.state = {
            totalDuration: duration * 60,
            originalDuration: duration * 60,
            participants: participants,
            currentParticipant: 1,
            startTime: Date.now() / 1000,
            extraTimeAdded: 0,
            remainingTime: 0,
            timePerParticipant: 0,
            elapsedTime: 0
        };

        // Вычисляем время на каждого участника
        this.calculateTimePerParticipant();

        this.showSetupSection(false);
        this.isRunning = true;
        this.startTimerUpdates();
        this.updateDisplay();
        this.showStatus('Встреча началась!', 'success');
    }

    calculateTimePerParticipant() {
        const remainingParticipants = this.state.participants - this.state.currentParticipant + 1;
        const remainingTime = this.state.totalDuration - this.state.elapsedTime;
        this.state.timePerParticipant = remainingTime / remainingParticipants;
        this.state.remainingTime = this.state.timePerParticipant;
    }

    getTimerState() {
        if (!this.isRunning) return;

        const currentTime = Date.now() / 1000;
        const elapsedForCurrent = currentTime - this.state.startTime;

        this.state.remainingTime = Math.max(0, this.state.timePerParticipant - elapsedForCurrent + this.state.extraTimeAdded);

        // Автоматический переход к следующему участнику при истечении времени
        if (this.state.remainingTime <= 0) {
            this.advanceToNextParticipant(true);
            return;
        }

        this.updateDisplay();
    }

    updateDisplay() {
        // Обновляем информацию об участниках
        document.getElementById('currentParticipant').textContent = this.state.currentParticipant;
        document.getElementById('totalParticipants').textContent = this.state.participants;

        // Обновляем таймер
        document.getElementById('timer').textContent = this.formatTime(this.state.remainingTime);
        document.getElementById('timePerParticipant').textContent = this.formatTime(this.state.timePerParticipant);

        // Обновляем прогресс-бар
        const progressPercentage = this.calculateProgressPercentage();
        const progressBar = document.getElementById('progress');
        progressBar.style.width = progressPercentage + '%';

        // Меняем цвет прогресс-бара
        if (progressPercentage < 20) {
            progressBar.style.background = 'linear-gradient(90deg, #f44336, #ff9800)';
        } else if (progressPercentage < 50) {
            progressBar.style.background = 'linear-gradient(90deg, #ff9800, #ffeb3b)';
        } else {
            progressBar.style.background = 'linear-gradient(90deg, #4CAF50, #8BC34A)';
        }

        // Обновляем информацию о встрече
        this.updateMeetingInfo();
    }

    calculateProgressPercentage() {
        if (this.state.timePerParticipant === 0) return 0;
        return Math.max(0, Math.min(100, (this.state.remainingTime / this.state.timePerParticipant) * 100));
    }

    addTime() {
        if (!this.isRunning) return;

        const extraTime = 30; // 30 секунд
        const remainingParticipantsAfterCurrent = this.state.participants - this.state.currentParticipant;

        if (remainingParticipantsAfterCurrent === 0) {
            // Последний участник - просто добавляем время
            this.state.extraTimeAdded += extraTime;
            this.showStatus(`Добавлено ${extraTime} секунд последнему участнику`, 'info');
        } else {
            // ✅ ИСПРАВЛЕННАЯ ЛОГИКА: Добавляем время текущему участнику
            this.state.extraTimeAdded += extraTime;

            // ✅ Время следующих участников уменьшается автоматически при расчете
            this.showStatus(
                `Добавлено ${extraTime} секунд текущему участнику. ` +
                `Время следующих участников будет уменьшено`,
                'info'
            );
        }

        this.updateDisplay();
    }

    nextParticipant() {
        if (!this.isRunning) return;

        this.advanceToNextParticipant(false);
    }

    advanceToNextParticipant(isAutomatic) {
        const currentTime = Date.now() / 1000;
        const elapsedForCurrent = currentTime - this.state.startTime;

        // Вычисляем фактически использованное время текущим участником
        const actualTimeUsed = Math.min(elapsedForCurrent, this.state.timePerParticipant + this.state.extraTimeAdded);

        // Обновляем общее прошедшее время
        this.state.elapsedTime += actualTimeUsed;

        // Вычисляем неиспользованное время
        const unusedTime = Math.max(0, this.state.timePerParticipant - actualTimeUsed + this.state.extraTimeAdded);

        // Сохраняем номер предыдущего участника для сообщения
        const previousParticipant = this.state.currentParticipant;

        // Переходим к следующему участнику
        this.state.currentParticipant++;
        this.state.startTime = currentTime;
        this.state.extraTimeAdded = 0;

        // Проверяем, не завершилась ли встреча
        if (this.state.currentParticipant > this.state.participants) {
            this.meetingFinished();
            return;
        }

        if (unusedTime > 0 && isAutomatic) {
            // ✅ ИСПРАВЛЕННАЯ ЛОГИКА: При автоматическом переходе неиспользованное время распределяется
            const remainingParticipants = this.state.participants - this.state.currentParticipant + 1;
            const extraTimePerParticipant = unusedTime / remainingParticipants;

            // Увеличиваем общее время встречи на неиспользованное время
            this.state.totalDuration += unusedTime;

            this.showStatus(
                `Участник ${previousParticipant} завершил досрочно. ` +
                `+${this.formatTime(extraTimePerParticipant)} каждому из ${remainingParticipants} участников`,
                'info'
            );
        } else if (!isAutomatic) {
            // При ручном переходе время просто перераспределяется
            this.showStatus(`Переход к участнику ${this.state.currentParticipant}`, 'success');
        }

        // Пересчитываем время для новых условий
        this.calculateTimePerParticipant();

        // Визуальный эффект
        const participantInfo = document.getElementById('currentParticipant');
        participantInfo.classList.add('participant-change');
        setTimeout(() => participantInfo.classList.remove('participant-change'), 500);

        this.updateDisplay();
    }

    updateMeetingInfo() {
        const totalMeetingTimeElement = document.getElementById('totalMeetingTime');
        const remainingMeetingTimeElement = document.getElementById('remainingMeetingTime');
        const remainingParticipantsElement = document.getElementById('remainingParticipants');

        if (totalMeetingTimeElement) {
            totalMeetingTimeElement.textContent = this.formatTime(this.state.totalDuration);
        }

        // Вычисляем оставшееся время встречи
        const currentTime = Date.now() / 1000;
        const elapsedTotal = this.state.elapsedTime + (currentTime - this.state.startTime);
        const remainingMeetingTime = Math.max(0, this.state.totalDuration - elapsedTotal);

        if (remainingMeetingTimeElement) {
            remainingMeetingTimeElement.textContent = this.formatTime(remainingMeetingTime);
        }

        if (remainingParticipantsElement) {
            remainingParticipantsElement.textContent = this.state.participants - this.state.currentParticipant + 1;
        }
    }

    stopMeeting() {
        this.isRunning = false;
        this.showSetupSection(true);
        this.showStatus('Встреча завершена', 'success');

        if (this.updateInterval) {
            clearInterval(this.updateInterval);
            this.updateInterval = null;
        }
    }

    meetingFinished() {
        this.isRunning = false;
        this.showSetupSection(true);
        this.showStatus('Все участники выступили! Встреча завершена.', 'success');

        if (this.updateInterval) {
            clearInterval(this.updateInterval);
            this.updateInterval = null;
        }
    }

    startTimerUpdates() {
        // Обновляем состояние каждую секунду
        this.updateInterval = setInterval(() => {
            if (this.isRunning) {
                this.getTimerState();
            }
        }, 1000);
    }

    showSetupSection(show) {
        const setupSection = document.getElementById('setupSection');
        const timerSection = document.getElementById('timerSection');

        if (show) {
            setupSection.style.display = 'block';
            timerSection.style.display = 'none';
        } else {
            setupSection.style.display = 'none';
            timerSection.style.display = 'block';
        }
    }

    showStatus(message, type) {
        const statusElement = document.getElementById('statusMessage');
        statusElement.textContent = message;
        statusElement.className = 'status-message';

        if (type === 'success') {
            statusElement.classList.add('status-success');
        } else if (type === 'warning') {
            statusElement.classList.add('status-warning');
        } else if (type === 'info') {
            statusElement.classList.add('status-info');
        }

        // Автоматически скрываем сообщение через 5 секунд
        setTimeout(() => {
            statusElement.textContent = '';
            statusElement.className = 'status-message';
        }, 5000);
    }

    formatTime(seconds) {
        const minutes = Math.floor(seconds / 60);
        const secs = Math.floor(seconds % 60);
        return `${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
}

// Инициализация при загрузке страницы
document.addEventListener('DOMContentLoaded', () => {
    new MeetingTimer();
});