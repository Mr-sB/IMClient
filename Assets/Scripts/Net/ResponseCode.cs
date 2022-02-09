namespace Net
{
    public enum ResponseCode : uint
    {
        Success = 0,
        RenameError = 101, //改名失败，名字重复
        ChatUserError = 102, //查无此人
    }
}