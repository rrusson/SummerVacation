# Copilot Instructions
<!-- 
This file provides custom instructions to GitHub Copilot for this repository.
-->

## Coding Standards
- Follow the existing code style in this repository
- Use meaningful variable and function names
- Add comments for complex logic
- Always use curly braces `{}` for all control structures (if, else, for, foreach, while, etc.), even wrapping single-line statements.
- Don't add comments when a function name, variable name, or block make the purpose of the code fairly obvious. Prefer "self-documenting" code with good naming.
- Insert a single blank line (CRLF) between method and function definitions. Adhere to StyleCop and commonly accepted C# (or TypeScript) formatting best practices throughout all code.
- Adhere to SOLID design principles and design patterns where applicable.
- Strive for small, single-responsibility methods. Refactor larger methods (>50 lines) into smaller pieces when practical.
- Create interfaces for public classes, especially any that might be used for dependency injection. Add XML documentation comments to the interfaces with <inheritdoc/> on classes implementing the interface.
- Add XML documentation comments to all public interfaces (and classes, methods, and properties not covered by <inheritdoc/>. Ensure comments are meaningful and up to date.
- Prefix private fields in a class with an underscore (`_`) and use camelCase.
- Prefer asynchronous methods over synchronous/blocking methods. Avoid blocking calls such as `.Result` or `.Wait()` on tasks.
- Always use `.ConfigureAwait(false)` on awaited tasks unless it explicitly requires the synchronization context.
- Write unit tests using the MSTest framework. For mocking dependencies, use MOQ. If mocking requires dependency injection and a suitable mock isn’t available, use Autofac.Extras.Moq’s `AutoMock.GetLoose`.
- Use the `var` keyword for local variable declarations when the type is clear from the right-hand side of the assignment; otherwise, use the explicit type.
- Use the `nameof` operator when referencing identifiers (such as property names, method names, etc.), instead of hardcoded strings.
- If a class contains many private methods, consider refactoring related functionality into separate classes to improve maintainability and cohesion.
- Use `IEnumerable<T>` or arrays for method parameters and return types when the collection is not intended to be modified. Use `List<T>` only when modification is required.
- Apply consistent exception handling practices across the codebase. Ensure that exceptions are meaningful, logged appropriately, and do not expose sensitive details.
- In agent mode, don't combine Powershell commands with "&&" since it fails in some environments.
