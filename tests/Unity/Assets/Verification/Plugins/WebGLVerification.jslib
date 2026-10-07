mergeInto(LibraryManager.library, {
    NavigathenaWebGLReport: function (message) {
        var result = document.getElementById("navigathena-verification-result");
        if (!result) {
            result = document.createElement("pre");
            result.id = "navigathena-verification-result";
            document.body.appendChild(result);
        }
        result.textContent = UTF8ToString(message);
    }
});
