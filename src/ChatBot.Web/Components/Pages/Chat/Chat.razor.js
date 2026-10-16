// Mantém a lista rolada até o fim enquanto o usuário estiver perto do final.
export function scrollToBottom(element) {
    if (!element) return;
    const distanceFromBottom = element.scrollHeight - element.scrollTop - element.clientHeight;
    if (distanceFromBottom < 200) {
        element.scrollTop = element.scrollHeight;
    }
}
