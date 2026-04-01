#define ICALL_TABLE_corlib 1

static int corlib_icall_indexes [] = {
194,
200,
201,
202,
203,
204,
205,
206,
207,
210,
211,
279,
280,
282,
305,
306,
307,
320,
321,
322,
323,
400,
401,
402,
405,
442,
443,
445,
447,
449,
451,
456,
464,
465,
466,
467,
468,
469,
470,
471,
472,
599,
607,
610,
612,
617,
618,
620,
621,
625,
626,
628,
629,
632,
633,
634,
637,
639,
642,
644,
646,
712,
714,
716,
725,
726,
727,
729,
735,
736,
737,
738,
739,
747,
748,
749,
753,
754,
756,
758,
957,
1110,
1111,
5741,
5742,
5744,
5745,
5746,
5747,
5748,
5750,
5752,
5754,
5762,
5764,
5768,
5769,
5771,
5773,
5775,
5786,
5795,
5796,
5798,
5799,
5800,
5801,
5802,
5804,
5806,
6823,
6827,
6829,
6830,
6831,
6832,
6961,
6962,
6963,
6964,
6984,
6985,
6986,
6988,
7029,
7104,
7106,
7116,
7117,
7118,
7119,
7396,
7398,
7399,
7422,
7440,
7446,
7453,
7463,
7466,
7542,
7550,
7552,
7558,
7572,
7592,
7593,
7601,
7603,
7610,
7611,
7614,
7616,
7621,
7627,
7628,
7635,
7637,
7649,
7652,
7653,
7654,
7665,
7674,
7680,
7681,
7682,
7684,
7685,
7703,
7705,
7719,
7738,
7757,
7787,
7788,
8208,
8340,
8553,
8554,
8557,
8558,
8559,
8564,
8617,
8907,
8908,
9105,
9107,
9108,
9916,
9937,
9944,
9946,
};
void ves_icall_System_Array_InternalCreate (int,int,int,int,int);
int ves_icall_System_Array_GetCorElementTypeOfElementType_raw (int,int);
int ves_icall_System_Array_CanChangePrimitive (int,int,int);
int ves_icall_System_Array_FastCopy_raw (int,int,int,int,int,int);
int ves_icall_System_Array_GetLength_raw (int,int,int);
int ves_icall_System_Array_GetLowerBound_raw (int,int,int);
void ves_icall_System_Array_GetGenericValue_icall (int,int,int);
int ves_icall_System_Array_GetValueImpl_raw (int,int,int);
void ves_icall_System_Array_SetGenericValue_icall (int,int,int);
void ves_icall_System_Array_SetValueImpl_raw (int,int,int,int);
void ves_icall_System_Array_SetValueRelaxedImpl_raw (int,int,int,int);
void ves_icall_System_Runtime_RuntimeImports_Memmove (int,int,int);
void ves_icall_System_Buffer_BulkMoveWithWriteBarrier (int,int,int,int);
void ves_icall_System_Runtime_RuntimeImports_ZeroMemory (int,int);
int ves_icall_System_Delegate_AllocDelegateLike_internal_raw (int,int);
int ves_icall_System_Delegate_CreateDelegate_internal_raw (int,int,int,int,int);
int ves_icall_System_Delegate_GetVirtualMethod_internal_raw (int,int);
int ves_icall_System_Enum_GetEnumValuesAndNames_raw (int,int,int,int);
void ves_icall_System_Enum_InternalBoxEnum_raw (int,int,int64_t,int);
int ves_icall_System_Enum_InternalGetCorElementType (int);
void ves_icall_System_Enum_InternalGetUnderlyingType_raw (int,int,int);
int ves_icall_System_Environment_get_ProcessorCount ();
int ves_icall_System_Environment_get_TickCount ();
int64_t ves_icall_System_Environment_get_TickCount64 ();
void ves_icall_System_Environment_FailFast_raw (int,int,int,int);
void ves_icall_System_GC_register_ephemeron_array_raw (int,int);
int ves_icall_System_GC_get_ephemeron_tombstone_raw (int);
void ves_icall_System_GC_SuppressFinalize_raw (int,int);
void ves_icall_System_GC_ReRegisterForFinalize_raw (int,int);
void ves_icall_System_GC_GetGCMemoryInfo (int,int,int,int,int,int);
int ves_icall_System_GC_AllocPinnedArray_raw (int,int,int);
int ves_icall_System_Object_MemberwiseClone_raw (int,int);
double ves_icall_System_Math_Ceiling (double);
double ves_icall_System_Math_Cos (double);
double ves_icall_System_Math_Floor (double);
double ves_icall_System_Math_Log10 (double);
double ves_icall_System_Math_Pow (double,double);
double ves_icall_System_Math_Sin (double);
double ves_icall_System_Math_Sqrt (double);
double ves_icall_System_Math_Tan (double);
double ves_icall_System_Math_ModF (double,int);
int ves_icall_RuntimeType_GetCorrespondingInflatedMethod_raw (int,int,int);
void ves_icall_RuntimeType_make_array_type_raw (int,int,int,int);
void ves_icall_RuntimeType_make_byref_type_raw (int,int,int);
void ves_icall_RuntimeType_make_pointer_type_raw (int,int,int);
void ves_icall_RuntimeType_MakeGenericType_raw (int,int,int,int);
int ves_icall_RuntimeType_GetMethodsByName_native_raw (int,int,int,int,int);
int ves_icall_RuntimeType_GetPropertiesByName_native_raw (int,int,int,int,int);
int ves_icall_RuntimeType_GetConstructors_native_raw (int,int,int);
int ves_icall_System_RuntimeType_CreateInstanceInternal_raw (int,int);
void ves_icall_RuntimeType_GetDeclaringMethod_raw (int,int,int);
void ves_icall_System_RuntimeType_getFullName_raw (int,int,int,int,int);
void ves_icall_RuntimeType_GetGenericArgumentsInternal_raw (int,int,int,int);
int ves_icall_RuntimeType_GetGenericParameterPosition (int);
int ves_icall_RuntimeType_GetEvents_native_raw (int,int,int,int);
int ves_icall_RuntimeType_GetFields_native_raw (int,int,int,int,int);
void ves_icall_RuntimeType_GetInterfaces_raw (int,int,int);
int ves_icall_RuntimeType_GetNestedTypes_native_raw (int,int,int,int,int);
void ves_icall_RuntimeType_GetDeclaringType_raw (int,int,int);
void ves_icall_RuntimeType_GetName_raw (int,int,int);
void ves_icall_RuntimeType_GetNamespace_raw (int,int,int);
int ves_icall_RuntimeTypeHandle_GetAttributes (int);
int ves_icall_RuntimeTypeHandle_GetMetadataToken_raw (int,int);
void ves_icall_RuntimeTypeHandle_GetGenericTypeDefinition_impl_raw (int,int,int);
int ves_icall_RuntimeTypeHandle_GetCorElementType (int);
int ves_icall_RuntimeTypeHandle_HasInstantiation (int);
int ves_icall_RuntimeTypeHandle_IsInstanceOfType_raw (int,int,int);
int ves_icall_RuntimeTypeHandle_HasReferences_raw (int,int);
int ves_icall_RuntimeTypeHandle_GetArrayRank_raw (int,int);
void ves_icall_RuntimeTypeHandle_GetAssembly_raw (int,int,int);
void ves_icall_RuntimeTypeHandle_GetElementType_raw (int,int,int);
void ves_icall_RuntimeTypeHandle_GetModule_raw (int,int,int);
void ves_icall_RuntimeTypeHandle_GetBaseType_raw (int,int,int);
int ves_icall_RuntimeTypeHandle_type_is_assignable_from_raw (int,int,int);
int ves_icall_RuntimeTypeHandle_IsGenericTypeDefinition (int);
int ves_icall_RuntimeTypeHandle_GetGenericParameterInfo_raw (int,int);
int ves_icall_RuntimeTypeHandle_is_subclass_of_raw (int,int,int);
int ves_icall_RuntimeTypeHandle_IsByRefLike_raw (int,int);
void ves_icall_System_RuntimeTypeHandle_internal_from_name_raw (int,int,int,int,int,int);
int ves_icall_System_String_FastAllocateString_raw (int,int);
int ves_icall_System_Type_internal_from_handle_raw (int,int);
int ves_icall_System_ValueType_InternalGetHashCode_raw (int,int,int);
int ves_icall_System_ValueType_Equals_raw (int,int,int,int);
int ves_icall_System_Threading_Interlocked_CompareExchange_Int (int,int,int);
void ves_icall_System_Threading_Interlocked_CompareExchange_Object (int,int,int,int);
int ves_icall_System_Threading_Interlocked_Decrement_Int (int);
int ves_icall_System_Threading_Interlocked_Increment_Int (int);
int64_t ves_icall_System_Threading_Interlocked_Increment_Long (int);
int ves_icall_System_Threading_Interlocked_Exchange_Int (int,int);
void ves_icall_System_Threading_Interlocked_Exchange_Object (int,int,int);
int64_t ves_icall_System_Threading_Interlocked_CompareExchange_Long (int,int64_t,int64_t);
int64_t ves_icall_System_Threading_Interlocked_Exchange_Long (int,int64_t);
int ves_icall_System_Threading_Interlocked_Add_Int (int,int);
void ves_icall_System_Threading_Monitor_Monitor_Enter_raw (int,int);
void mono_monitor_exit_icall_raw (int,int);
int ves_icall_System_Threading_Monitor_Monitor_test_synchronised_raw (int,int);
void ves_icall_System_Threading_Monitor_Monitor_pulse_raw (int,int);
void ves_icall_System_Threading_Monitor_Monitor_pulse_all_raw (int,int);
int ves_icall_System_Threading_Monitor_Monitor_wait_raw (int,int,int,int);
void ves_icall_System_Threading_Monitor_Monitor_try_enter_with_atomic_var_raw (int,int,int,int,int);
int ves_icall_System_Threading_Thread_GetCurrentProcessorNumber_raw (int);
void ves_icall_System_Threading_Thread_InitInternal_raw (int,int);
int ves_icall_System_Threading_Thread_GetCurrentThread ();
void ves_icall_System_Threading_InternalThread_Thread_free_internal_raw (int,int);
int ves_icall_System_Threading_Thread_GetState_raw (int,int);
void ves_icall_System_Threading_Thread_SetState_raw (int,int,int);
void ves_icall_System_Threading_Thread_ClrState_raw (int,int,int);
void ves_icall_System_Threading_Thread_SetName_icall_raw (int,int,int,int);
int ves_icall_System_Threading_Thread_YieldInternal ();
void ves_icall_System_Threading_Thread_SetPriority_raw (int,int,int);
void ves_icall_System_Runtime_Loader_AssemblyLoadContext_PrepareForAssemblyLoadContextRelease_raw (int,int,int);
int ves_icall_System_Runtime_Loader_AssemblyLoadContext_GetLoadContextForAssembly_raw (int,int);
int ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalLoadFile_raw (int,int,int,int);
int ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalInitializeNativeALC_raw (int,int,int,int,int);
int ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalLoadFromStream_raw (int,int,int,int,int,int);
int ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalGetLoadedAssemblies_raw (int);
int ves_icall_System_GCHandle_InternalAlloc_raw (int,int,int);
void ves_icall_System_GCHandle_InternalFree_raw (int,int);
int ves_icall_System_GCHandle_InternalGet_raw (int,int);
void ves_icall_System_GCHandle_InternalSet_raw (int,int,int);
int ves_icall_System_Runtime_InteropServices_Marshal_GetLastPInvokeError ();
void ves_icall_System_Runtime_InteropServices_Marshal_SetLastPInvokeError (int);
void ves_icall_System_Runtime_InteropServices_Marshal_StructureToPtr_raw (int,int,int,int);
int ves_icall_System_Runtime_InteropServices_Marshal_SizeOfHelper_raw (int,int,int);
int ves_icall_System_Runtime_InteropServices_NativeLibrary_LoadByName_raw (int,int,int,int,int,int);
int mono_object_hash_icall_raw (int,int);
int ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_GetObjectValue_raw (int,int);
int ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_GetUninitializedObjectInternal_raw (int,int);
void ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_InitializeArray_raw (int,int,int);
int ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_GetSpanDataFrom_raw (int,int,int,int);
int ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_SufficientExecutionStack ();
int ves_icall_System_Reflection_Assembly_GetEntryAssembly_raw (int);
int ves_icall_System_Reflection_Assembly_InternalLoad_raw (int,int,int,int);
int ves_icall_System_Reflection_Assembly_InternalGetType_raw (int,int,int,int,int,int);
int ves_icall_System_Reflection_AssemblyName_GetNativeName (int);
int ves_icall_MonoCustomAttrs_GetCustomAttributesInternal_raw (int,int,int,int);
int ves_icall_MonoCustomAttrs_GetCustomAttributesDataInternal_raw (int,int);
int ves_icall_MonoCustomAttrs_IsDefinedInternal_raw (int,int,int);
int ves_icall_System_Reflection_FieldInfo_internal_from_handle_type_raw (int,int,int);
int ves_icall_System_Reflection_FieldInfo_get_marshal_info_raw (int,int);
void ves_icall_System_Reflection_RuntimeAssembly_GetExportedTypes_raw (int,int,int);
void ves_icall_System_Reflection_RuntimeAssembly_GetInfo_raw (int,int,int,int);
void ves_icall_System_Reflection_Assembly_GetManifestModuleInternal_raw (int,int,int);
void ves_icall_System_Reflection_RuntimeCustomAttributeData_ResolveArgumentsInternal_raw (int,int,int,int,int,int,int);
void ves_icall_RuntimeEventInfo_get_event_info_raw (int,int,int);
int ves_icall_reflection_get_token_raw (int,int);
int ves_icall_System_Reflection_EventInfo_internal_from_handle_type_raw (int,int,int);
int ves_icall_RuntimeFieldInfo_ResolveType_raw (int,int);
int ves_icall_RuntimeFieldInfo_GetParentType_raw (int,int,int);
int ves_icall_RuntimeFieldInfo_GetFieldOffset_raw (int,int);
int ves_icall_RuntimeFieldInfo_GetValueInternal_raw (int,int,int);
void ves_icall_RuntimeFieldInfo_SetValueInternal_raw (int,int,int,int);
int ves_icall_RuntimeFieldInfo_GetRawConstantValue_raw (int,int);
int ves_icall_reflection_get_token_raw (int,int);
void ves_icall_get_method_info_raw (int,int,int);
int ves_icall_get_method_attributes (int);
int ves_icall_System_Reflection_MonoMethodInfo_get_parameter_info_raw (int,int,int);
int ves_icall_System_MonoMethodInfo_get_retval_marshal_raw (int,int);
int ves_icall_System_Reflection_RuntimeMethodInfo_GetMethodFromHandleInternalType_native_raw (int,int,int,int);
int ves_icall_RuntimeMethodInfo_get_name_raw (int,int);
int ves_icall_RuntimeMethodInfo_get_base_method_raw (int,int,int);
int ves_icall_reflection_get_token_raw (int,int);
int ves_icall_InternalInvoke_raw (int,int,int,int,int);
void ves_icall_RuntimeMethodInfo_GetPInvoke_raw (int,int,int,int,int);
int ves_icall_RuntimeMethodInfo_MakeGenericMethod_impl_raw (int,int,int);
int ves_icall_RuntimeMethodInfo_GetGenericArguments_raw (int,int);
int ves_icall_RuntimeMethodInfo_GetGenericMethodDefinition_raw (int,int);
int ves_icall_RuntimeMethodInfo_get_IsGenericMethodDefinition_raw (int,int);
int ves_icall_RuntimeMethodInfo_get_IsGenericMethod_raw (int,int);
void ves_icall_InvokeClassConstructor_raw (int,int);
int ves_icall_InternalInvoke_raw (int,int,int,int,int);
int ves_icall_reflection_get_token_raw (int,int);
int ves_icall_System_Reflection_RuntimeModule_ResolveMethodToken_raw (int,int,int,int,int,int);
void ves_icall_RuntimePropertyInfo_get_property_info_raw (int,int,int,int);
int ves_icall_reflection_get_token_raw (int,int);
int ves_icall_System_Reflection_RuntimePropertyInfo_internal_from_handle_type_raw (int,int,int);
void ves_icall_AssemblyBuilder_basic_init_raw (int,int);
void ves_icall_DynamicMethod_create_dynamic_method_raw (int,int);
void ves_icall_ModuleBuilder_basic_init_raw (int,int);
void ves_icall_ModuleBuilder_set_wrappers_type_raw (int,int,int);
int ves_icall_ModuleBuilder_getUSIndex_raw (int,int,int);
int ves_icall_ModuleBuilder_getToken_raw (int,int,int,int);
int ves_icall_ModuleBuilder_getMethodToken_raw (int,int,int,int);
void ves_icall_ModuleBuilder_RegisterToken_raw (int,int,int,int);
int ves_icall_TypeBuilder_create_runtime_class_raw (int,int);
int ves_icall_System_IO_Stream_HasOverriddenBeginEndRead_raw (int,int);
int ves_icall_System_IO_Stream_HasOverriddenBeginEndWrite_raw (int,int);
int ves_icall_System_Diagnostics_Debugger_IsAttached_internal ();
int ves_icall_System_Diagnostics_Debugger_IsLogging ();
void ves_icall_System_Diagnostics_Debugger_Log (int,int,int);
int ves_icall_Mono_RuntimeClassHandle_GetTypeFromClass (int);
void ves_icall_Mono_RuntimeGPtrArrayHandle_GPtrArrayFree (int);
int ves_icall_Mono_SafeStringMarshal_StringToUtf8 (int);
void ves_icall_Mono_SafeStringMarshal_GFree (int);
static void *corlib_icall_funcs [] = {
// token 194,
ves_icall_System_Array_InternalCreate,
// token 200,
ves_icall_System_Array_GetCorElementTypeOfElementType_raw,
// token 201,
ves_icall_System_Array_CanChangePrimitive,
// token 202,
ves_icall_System_Array_FastCopy_raw,
// token 203,
ves_icall_System_Array_GetLength_raw,
// token 204,
ves_icall_System_Array_GetLowerBound_raw,
// token 205,
ves_icall_System_Array_GetGenericValue_icall,
// token 206,
ves_icall_System_Array_GetValueImpl_raw,
// token 207,
ves_icall_System_Array_SetGenericValue_icall,
// token 210,
ves_icall_System_Array_SetValueImpl_raw,
// token 211,
ves_icall_System_Array_SetValueRelaxedImpl_raw,
// token 279,
ves_icall_System_Runtime_RuntimeImports_Memmove,
// token 280,
ves_icall_System_Buffer_BulkMoveWithWriteBarrier,
// token 282,
ves_icall_System_Runtime_RuntimeImports_ZeroMemory,
// token 305,
ves_icall_System_Delegate_AllocDelegateLike_internal_raw,
// token 306,
ves_icall_System_Delegate_CreateDelegate_internal_raw,
// token 307,
ves_icall_System_Delegate_GetVirtualMethod_internal_raw,
// token 320,
ves_icall_System_Enum_GetEnumValuesAndNames_raw,
// token 321,
ves_icall_System_Enum_InternalBoxEnum_raw,
// token 322,
ves_icall_System_Enum_InternalGetCorElementType,
// token 323,
ves_icall_System_Enum_InternalGetUnderlyingType_raw,
// token 400,
ves_icall_System_Environment_get_ProcessorCount,
// token 401,
ves_icall_System_Environment_get_TickCount,
// token 402,
ves_icall_System_Environment_get_TickCount64,
// token 405,
ves_icall_System_Environment_FailFast_raw,
// token 442,
ves_icall_System_GC_register_ephemeron_array_raw,
// token 443,
ves_icall_System_GC_get_ephemeron_tombstone_raw,
// token 445,
ves_icall_System_GC_SuppressFinalize_raw,
// token 447,
ves_icall_System_GC_ReRegisterForFinalize_raw,
// token 449,
ves_icall_System_GC_GetGCMemoryInfo,
// token 451,
ves_icall_System_GC_AllocPinnedArray_raw,
// token 456,
ves_icall_System_Object_MemberwiseClone_raw,
// token 464,
ves_icall_System_Math_Ceiling,
// token 465,
ves_icall_System_Math_Cos,
// token 466,
ves_icall_System_Math_Floor,
// token 467,
ves_icall_System_Math_Log10,
// token 468,
ves_icall_System_Math_Pow,
// token 469,
ves_icall_System_Math_Sin,
// token 470,
ves_icall_System_Math_Sqrt,
// token 471,
ves_icall_System_Math_Tan,
// token 472,
ves_icall_System_Math_ModF,
// token 599,
ves_icall_RuntimeType_GetCorrespondingInflatedMethod_raw,
// token 607,
ves_icall_RuntimeType_make_array_type_raw,
// token 610,
ves_icall_RuntimeType_make_byref_type_raw,
// token 612,
ves_icall_RuntimeType_make_pointer_type_raw,
// token 617,
ves_icall_RuntimeType_MakeGenericType_raw,
// token 618,
ves_icall_RuntimeType_GetMethodsByName_native_raw,
// token 620,
ves_icall_RuntimeType_GetPropertiesByName_native_raw,
// token 621,
ves_icall_RuntimeType_GetConstructors_native_raw,
// token 625,
ves_icall_System_RuntimeType_CreateInstanceInternal_raw,
// token 626,
ves_icall_RuntimeType_GetDeclaringMethod_raw,
// token 628,
ves_icall_System_RuntimeType_getFullName_raw,
// token 629,
ves_icall_RuntimeType_GetGenericArgumentsInternal_raw,
// token 632,
ves_icall_RuntimeType_GetGenericParameterPosition,
// token 633,
ves_icall_RuntimeType_GetEvents_native_raw,
// token 634,
ves_icall_RuntimeType_GetFields_native_raw,
// token 637,
ves_icall_RuntimeType_GetInterfaces_raw,
// token 639,
ves_icall_RuntimeType_GetNestedTypes_native_raw,
// token 642,
ves_icall_RuntimeType_GetDeclaringType_raw,
// token 644,
ves_icall_RuntimeType_GetName_raw,
// token 646,
ves_icall_RuntimeType_GetNamespace_raw,
// token 712,
ves_icall_RuntimeTypeHandle_GetAttributes,
// token 714,
ves_icall_RuntimeTypeHandle_GetMetadataToken_raw,
// token 716,
ves_icall_RuntimeTypeHandle_GetGenericTypeDefinition_impl_raw,
// token 725,
ves_icall_RuntimeTypeHandle_GetCorElementType,
// token 726,
ves_icall_RuntimeTypeHandle_HasInstantiation,
// token 727,
ves_icall_RuntimeTypeHandle_IsInstanceOfType_raw,
// token 729,
ves_icall_RuntimeTypeHandle_HasReferences_raw,
// token 735,
ves_icall_RuntimeTypeHandle_GetArrayRank_raw,
// token 736,
ves_icall_RuntimeTypeHandle_GetAssembly_raw,
// token 737,
ves_icall_RuntimeTypeHandle_GetElementType_raw,
// token 738,
ves_icall_RuntimeTypeHandle_GetModule_raw,
// token 739,
ves_icall_RuntimeTypeHandle_GetBaseType_raw,
// token 747,
ves_icall_RuntimeTypeHandle_type_is_assignable_from_raw,
// token 748,
ves_icall_RuntimeTypeHandle_IsGenericTypeDefinition,
// token 749,
ves_icall_RuntimeTypeHandle_GetGenericParameterInfo_raw,
// token 753,
ves_icall_RuntimeTypeHandle_is_subclass_of_raw,
// token 754,
ves_icall_RuntimeTypeHandle_IsByRefLike_raw,
// token 756,
ves_icall_System_RuntimeTypeHandle_internal_from_name_raw,
// token 758,
ves_icall_System_String_FastAllocateString_raw,
// token 957,
ves_icall_System_Type_internal_from_handle_raw,
// token 1110,
ves_icall_System_ValueType_InternalGetHashCode_raw,
// token 1111,
ves_icall_System_ValueType_Equals_raw,
// token 5741,
ves_icall_System_Threading_Interlocked_CompareExchange_Int,
// token 5742,
ves_icall_System_Threading_Interlocked_CompareExchange_Object,
// token 5744,
ves_icall_System_Threading_Interlocked_Decrement_Int,
// token 5745,
ves_icall_System_Threading_Interlocked_Increment_Int,
// token 5746,
ves_icall_System_Threading_Interlocked_Increment_Long,
// token 5747,
ves_icall_System_Threading_Interlocked_Exchange_Int,
// token 5748,
ves_icall_System_Threading_Interlocked_Exchange_Object,
// token 5750,
ves_icall_System_Threading_Interlocked_CompareExchange_Long,
// token 5752,
ves_icall_System_Threading_Interlocked_Exchange_Long,
// token 5754,
ves_icall_System_Threading_Interlocked_Add_Int,
// token 5762,
ves_icall_System_Threading_Monitor_Monitor_Enter_raw,
// token 5764,
mono_monitor_exit_icall_raw,
// token 5768,
ves_icall_System_Threading_Monitor_Monitor_test_synchronised_raw,
// token 5769,
ves_icall_System_Threading_Monitor_Monitor_pulse_raw,
// token 5771,
ves_icall_System_Threading_Monitor_Monitor_pulse_all_raw,
// token 5773,
ves_icall_System_Threading_Monitor_Monitor_wait_raw,
// token 5775,
ves_icall_System_Threading_Monitor_Monitor_try_enter_with_atomic_var_raw,
// token 5786,
ves_icall_System_Threading_Thread_GetCurrentProcessorNumber_raw,
// token 5795,
ves_icall_System_Threading_Thread_InitInternal_raw,
// token 5796,
ves_icall_System_Threading_Thread_GetCurrentThread,
// token 5798,
ves_icall_System_Threading_InternalThread_Thread_free_internal_raw,
// token 5799,
ves_icall_System_Threading_Thread_GetState_raw,
// token 5800,
ves_icall_System_Threading_Thread_SetState_raw,
// token 5801,
ves_icall_System_Threading_Thread_ClrState_raw,
// token 5802,
ves_icall_System_Threading_Thread_SetName_icall_raw,
// token 5804,
ves_icall_System_Threading_Thread_YieldInternal,
// token 5806,
ves_icall_System_Threading_Thread_SetPriority_raw,
// token 6823,
ves_icall_System_Runtime_Loader_AssemblyLoadContext_PrepareForAssemblyLoadContextRelease_raw,
// token 6827,
ves_icall_System_Runtime_Loader_AssemblyLoadContext_GetLoadContextForAssembly_raw,
// token 6829,
ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalLoadFile_raw,
// token 6830,
ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalInitializeNativeALC_raw,
// token 6831,
ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalLoadFromStream_raw,
// token 6832,
ves_icall_System_Runtime_Loader_AssemblyLoadContext_InternalGetLoadedAssemblies_raw,
// token 6961,
ves_icall_System_GCHandle_InternalAlloc_raw,
// token 6962,
ves_icall_System_GCHandle_InternalFree_raw,
// token 6963,
ves_icall_System_GCHandle_InternalGet_raw,
// token 6964,
ves_icall_System_GCHandle_InternalSet_raw,
// token 6984,
ves_icall_System_Runtime_InteropServices_Marshal_GetLastPInvokeError,
// token 6985,
ves_icall_System_Runtime_InteropServices_Marshal_SetLastPInvokeError,
// token 6986,
ves_icall_System_Runtime_InteropServices_Marshal_StructureToPtr_raw,
// token 6988,
ves_icall_System_Runtime_InteropServices_Marshal_SizeOfHelper_raw,
// token 7029,
ves_icall_System_Runtime_InteropServices_NativeLibrary_LoadByName_raw,
// token 7104,
mono_object_hash_icall_raw,
// token 7106,
ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_GetObjectValue_raw,
// token 7116,
ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_GetUninitializedObjectInternal_raw,
// token 7117,
ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_InitializeArray_raw,
// token 7118,
ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_GetSpanDataFrom_raw,
// token 7119,
ves_icall_System_Runtime_CompilerServices_RuntimeHelpers_SufficientExecutionStack,
// token 7396,
ves_icall_System_Reflection_Assembly_GetEntryAssembly_raw,
// token 7398,
ves_icall_System_Reflection_Assembly_InternalLoad_raw,
// token 7399,
ves_icall_System_Reflection_Assembly_InternalGetType_raw,
// token 7422,
ves_icall_System_Reflection_AssemblyName_GetNativeName,
// token 7440,
ves_icall_MonoCustomAttrs_GetCustomAttributesInternal_raw,
// token 7446,
ves_icall_MonoCustomAttrs_GetCustomAttributesDataInternal_raw,
// token 7453,
ves_icall_MonoCustomAttrs_IsDefinedInternal_raw,
// token 7463,
ves_icall_System_Reflection_FieldInfo_internal_from_handle_type_raw,
// token 7466,
ves_icall_System_Reflection_FieldInfo_get_marshal_info_raw,
// token 7542,
ves_icall_System_Reflection_RuntimeAssembly_GetExportedTypes_raw,
// token 7550,
ves_icall_System_Reflection_RuntimeAssembly_GetInfo_raw,
// token 7552,
ves_icall_System_Reflection_Assembly_GetManifestModuleInternal_raw,
// token 7558,
ves_icall_System_Reflection_RuntimeCustomAttributeData_ResolveArgumentsInternal_raw,
// token 7572,
ves_icall_RuntimeEventInfo_get_event_info_raw,
// token 7592,
ves_icall_reflection_get_token_raw,
// token 7593,
ves_icall_System_Reflection_EventInfo_internal_from_handle_type_raw,
// token 7601,
ves_icall_RuntimeFieldInfo_ResolveType_raw,
// token 7603,
ves_icall_RuntimeFieldInfo_GetParentType_raw,
// token 7610,
ves_icall_RuntimeFieldInfo_GetFieldOffset_raw,
// token 7611,
ves_icall_RuntimeFieldInfo_GetValueInternal_raw,
// token 7614,
ves_icall_RuntimeFieldInfo_SetValueInternal_raw,
// token 7616,
ves_icall_RuntimeFieldInfo_GetRawConstantValue_raw,
// token 7621,
ves_icall_reflection_get_token_raw,
// token 7627,
ves_icall_get_method_info_raw,
// token 7628,
ves_icall_get_method_attributes,
// token 7635,
ves_icall_System_Reflection_MonoMethodInfo_get_parameter_info_raw,
// token 7637,
ves_icall_System_MonoMethodInfo_get_retval_marshal_raw,
// token 7649,
ves_icall_System_Reflection_RuntimeMethodInfo_GetMethodFromHandleInternalType_native_raw,
// token 7652,
ves_icall_RuntimeMethodInfo_get_name_raw,
// token 7653,
ves_icall_RuntimeMethodInfo_get_base_method_raw,
// token 7654,
ves_icall_reflection_get_token_raw,
// token 7665,
ves_icall_InternalInvoke_raw,
// token 7674,
ves_icall_RuntimeMethodInfo_GetPInvoke_raw,
// token 7680,
ves_icall_RuntimeMethodInfo_MakeGenericMethod_impl_raw,
// token 7681,
ves_icall_RuntimeMethodInfo_GetGenericArguments_raw,
// token 7682,
ves_icall_RuntimeMethodInfo_GetGenericMethodDefinition_raw,
// token 7684,
ves_icall_RuntimeMethodInfo_get_IsGenericMethodDefinition_raw,
// token 7685,
ves_icall_RuntimeMethodInfo_get_IsGenericMethod_raw,
// token 7703,
ves_icall_InvokeClassConstructor_raw,
// token 7705,
ves_icall_InternalInvoke_raw,
// token 7719,
ves_icall_reflection_get_token_raw,
// token 7738,
ves_icall_System_Reflection_RuntimeModule_ResolveMethodToken_raw,
// token 7757,
ves_icall_RuntimePropertyInfo_get_property_info_raw,
// token 7787,
ves_icall_reflection_get_token_raw,
// token 7788,
ves_icall_System_Reflection_RuntimePropertyInfo_internal_from_handle_type_raw,
// token 8208,
ves_icall_AssemblyBuilder_basic_init_raw,
// token 8340,
ves_icall_DynamicMethod_create_dynamic_method_raw,
// token 8553,
ves_icall_ModuleBuilder_basic_init_raw,
// token 8554,
ves_icall_ModuleBuilder_set_wrappers_type_raw,
// token 8557,
ves_icall_ModuleBuilder_getUSIndex_raw,
// token 8558,
ves_icall_ModuleBuilder_getToken_raw,
// token 8559,
ves_icall_ModuleBuilder_getMethodToken_raw,
// token 8564,
ves_icall_ModuleBuilder_RegisterToken_raw,
// token 8617,
ves_icall_TypeBuilder_create_runtime_class_raw,
// token 8907,
ves_icall_System_IO_Stream_HasOverriddenBeginEndRead_raw,
// token 8908,
ves_icall_System_IO_Stream_HasOverriddenBeginEndWrite_raw,
// token 9105,
ves_icall_System_Diagnostics_Debugger_IsAttached_internal,
// token 9107,
ves_icall_System_Diagnostics_Debugger_IsLogging,
// token 9108,
ves_icall_System_Diagnostics_Debugger_Log,
// token 9916,
ves_icall_Mono_RuntimeClassHandle_GetTypeFromClass,
// token 9937,
ves_icall_Mono_RuntimeGPtrArrayHandle_GPtrArrayFree,
// token 9944,
ves_icall_Mono_SafeStringMarshal_StringToUtf8,
// token 9946,
ves_icall_Mono_SafeStringMarshal_GFree,
};
static uint8_t corlib_icall_handles [] = {
0,
1,
0,
1,
1,
1,
0,
1,
0,
1,
1,
0,
0,
0,
1,
1,
1,
1,
1,
0,
1,
0,
0,
0,
1,
1,
1,
1,
1,
0,
1,
1,
0,
0,
0,
0,
0,
0,
0,
0,
0,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
0,
1,
1,
1,
1,
1,
1,
1,
0,
1,
1,
0,
0,
1,
1,
1,
1,
1,
1,
1,
1,
0,
1,
1,
1,
1,
1,
1,
1,
1,
0,
0,
0,
0,
0,
0,
0,
0,
0,
0,
1,
1,
1,
1,
1,
1,
1,
1,
1,
0,
1,
1,
1,
1,
1,
0,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
0,
0,
1,
1,
1,
1,
1,
1,
1,
1,
0,
1,
1,
1,
0,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
0,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
1,
0,
0,
0,
0,
0,
0,
0,
};
