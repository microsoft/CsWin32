namespace System.Runtime.CompilerServices
{
	/// <summary>
	/// Specifies the priority of a member in overload resolution.
	/// When unspecified, the default priority is 0.
	/// </summary>
	[global::System.AttributeUsage(global::System.AttributeTargets.Constructor | global::System.AttributeTargets.Method | global::System.AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
	internal sealed class OverloadResolutionPriorityAttribute : global::System.Attribute
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="OverloadResolutionPriorityAttribute"/> class.
		/// </summary>
		/// <param name="priority">The priority of the attributed member. Higher numbers are prioritized, lower numbers are deprioritized. 0 is the default if no attribute is present.</param>
		public OverloadResolutionPriorityAttribute(int priority)
		{
			this.Priority = priority;
		}

		/// <summary>
		/// The priority of the member.
		/// </summary>
		public int Priority { get; }
	}
}
