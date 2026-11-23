/* Mensagens padrão da validação no navegador, em português do Brasil. */
(function ($) {
    if (!$ || !$.validator) return;

    $.extend($.validator.messages, {
        required: "Este campo é obrigatório.",
        remote: "Confira este campo.",
        email: "Informe um endereço de e-mail válido.",
        url: "Informe um endereço válido.",
        date: "Informe uma data válida.",
        dateISO: "Informe uma data válida (AAAA-MM-DD).",
        number: "Informe um número válido.",
        digits: "Informe apenas números.",
        creditcard: "Informe um número de cartão válido.",
        equalTo: "Informe o mesmo valor novamente.",
        maxlength: $.validator.format("Informe no máximo {0} caracteres."),
        minlength: $.validator.format("Informe pelo menos {0} caracteres."),
        rangelength: $.validator.format("Informe entre {0} e {1} caracteres."),
        range: $.validator.format("Informe um valor entre {0} e {1}."),
        max: $.validator.format("Informe um valor menor ou igual a {0}."),
        min: $.validator.format("Informe um valor maior ou igual a {0}."),
        step: "Informe um múltiplo válido deste valor."
    });
})(window.jQuery);
