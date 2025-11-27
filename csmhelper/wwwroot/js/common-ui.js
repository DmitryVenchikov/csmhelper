// wwwroot/js/common-ui.js
function showMessage(message, type) {
    const statusElement = document.getElementById('statusMessage');
    if (!statusElement) {
        console.log(`${type}: ${message}`);
        return;
    }

    statusElement.textContent = message;
    statusElement.className = 'alert';

    if (type === 'success') {
        statusElement.classList.add('alert-success');
    } else if (type === 'error') {
        statusElement.classList.add('alert-danger');
    } else if (type === 'warning') {
        statusElement.classList.add('alert-warning');
    }

    statusElement.classList.remove('d-none');

    if (type !== 'error') {
        setTimeout(() => {
            statusElement.classList.add('d-none');
        }, 5000);
    }
}

function toggleButton(buttonId, spinnerId, loading) {
    const button = document.getElementById(buttonId);
    const spinner = document.getElementById(spinnerId);

    if (loading) {
        button.disabled = true;
        if (spinner) spinner.classList.remove('d-none');
    } else {
        button.disabled = false;
        if (spinner) spinner.classList.add('d-none');
    }
}