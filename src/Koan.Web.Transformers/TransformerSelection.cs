using System;

namespace Koan.Web.Transformers;

public sealed class TransformerSelection
{
	internal TransformerSelection(Type entityType, string contentType, IEntityTransformerInvoker invoker)
	{
		EntityType = entityType;
		ContentType = contentType;
		Invoker = invoker;
	}

	public Type EntityType { get; }

	public string ContentType { get; }

	internal IEntityTransformerInvoker Invoker { get; }
}
