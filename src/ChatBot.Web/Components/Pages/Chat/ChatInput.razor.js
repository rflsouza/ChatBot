// Enter envia, Shift+Enter quebra linha, e o textarea cresce até 6 linhas.
export function init(textarea) {
    textarea.focus();

    const resize = () => {
        textarea.style.height = 'auto';
        textarea.style.height = Math.min(textarea.scrollHeight, 6 * 24) + 'px';
    };

    textarea.addEventListener('input', resize);

    textarea.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            // Garante que o @bind receba o valor antes do submit.
            textarea.dispatchEvent(new Event('change', { bubbles: true }));
            textarea.closest('form').requestSubmit();
            setTimeout(resize, 0);
        }
    });
}
