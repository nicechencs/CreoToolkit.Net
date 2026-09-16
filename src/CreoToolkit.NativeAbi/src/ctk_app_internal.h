#ifndef CTK_APP_INTERNAL_H
#define CTK_APP_INTERNAL_H

// L1 命令桥内部接缝:命令分发内核 ctk_invoke_token。
// 生产路径由 Creo 命令蹦床(ctk_cmd_action_trampoline)调用;
// 脱 Creo 测试路径由独立测试钩子调用。该函数对错误线程和已终止桥接
// 静默 inert，绝不读取路由表或托管函数指针。
// 仅供 ctk_app 实现 TU 与其单测共享,置于 src/ 而非公开 include/,不属公开 ABI。
int ctk_invoke_token(void* cmd);

#endif /* CTK_APP_INTERNAL_H */
