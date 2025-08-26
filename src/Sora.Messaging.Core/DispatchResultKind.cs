namespace Sora.Messaging;

public enum DispatchResultKind { Success, DuplicateSkipped, NoHandler, DeserializationSkipped, Failure }